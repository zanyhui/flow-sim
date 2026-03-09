using System;

namespace FlowSim.Models
{
    /// <summary>
    /// 两支流汇合到一条干流的顺序求解器，支持一次精算迭代以改善汇流点水位连续性。
    /// <para>
    /// 算法流程（初算 + 可选精算）：
    /// <list type="number">
    ///   <item>【初算】独立求解支流1、支流2（及可选的干流上游段），下游边界均取正常水深；</item>
    ///   <item>【初算】将各出口流量叠加，求解干流（下游段），得到汇流点初算水位过程线；</item>
    ///   <item>【精算】以汇流点初算水位作为各上游段的下游水位边界，重新求解各上游段；</item>
    ///   <item>【精算】重新叠加改善后的出口流量，再次求解干流（下游段），完成精算。</item>
    /// </list>
    /// 精算迭代使汇流点水位在各上游段与下游干流之间保持连续，物理一致性更强。
    /// 当未提供上游段精算工厂时，退化为原始"非耦合法"（仅执行初算）。
    /// </para>
    /// <para>
    /// 物理约束满足情况：
    /// <list type="bullet">
    ///   <item>连续性方程（流量守恒）：Q干流 = ΣQ_上游出口，始终满足；</item>
    ///   <item>汇流点水位连续条件：初算不满足，精算后近似满足（一次迭代）；</item>
    ///   <item>动量方程：按 1D 惯例忽略汇口动量损失项，标准工程做法。</item>
    /// </list>
    /// </para>
    /// </summary>
    public class JunctionSolver
    {
        // ── 当前活跃的上游段求解器（精算后会替换为精算版本）──
        private Solver _trib1Solver;
        private Solver _trib2Solver;
        private Solver? _upstreamMainSolver;

        private readonly Func<Hydrograph, Solver> _mainSolverFactory;

        // ── 精算工厂（可选）：接受下游水位边界，返回新的求解器实例 ──
        private readonly Func<Boundary, Solver>? _trib1RefinedFactory;
        private readonly Func<Boundary, Solver>? _trib2RefinedFactory;
        private readonly Func<Boundary, Solver>? _upMainRefinedFactory;

        /// <summary>
        /// 汇流点水位边界中允许的最小出口水深（m）。
        /// 当汇流点水位低于上游段出口床底高程时（即上游段高于汇流点水面），
        /// 使用此值作为物理下限，防止水深为零或负值导致求解器发散。
        /// </summary>
        private const double MinimumOutletDepth = 0.001;   // 1 mm

        /// <summary>
        /// 时间步长一致性校验的相对容差。
        /// 各上游段求解器的 dt 偏差超过此比例则认为不一致（防止流量时序错位）。
        /// </summary>
        private const double TimeStepRelTol = 1e-6;

        /// <summary>支流1求解器（精算完成后返回精算版本）。</summary>
        public Solver Tributary1Solver => _trib1Solver;

        /// <summary>支流2求解器（精算完成后返回精算版本）。</summary>
        public Solver Tributary2Solver => _trib2Solver;

        /// <summary>
        /// 干流上游段求解器（中游汇流时使用；精算完成后返回精算版本；否则为 null）。
        /// </summary>
        public Solver? UpstreamMainSolver => _upstreamMainSolver;

        /// <summary>干流（下游段）求解器，<see cref="Run"/> 执行后才赋值。</summary>
        public Solver? MainSolver { get; private set; }

        /// <summary>仿真是否成功完成。</summary>
        public bool Solved => MainSolver?.Solved ?? false;

        /// <summary>
        /// 汇流点绝对水位过程线（m）。精算完成后为精算结果；仅初算时为初算结果。
        /// 取干流下游段首节点的水位（= Depth + BedProfile[0]）。
        /// </summary>
        public double[]? JunctionStage { get; private set; }

        /// <summary>是否已完成精算迭代。</summary>
        public bool IsRefined { get; private set; }

        /// <summary>阶段日志回调。</summary>
        public Action<string>? LogCallback { get; set; }

        /// <summary>每时步进度回调，参数为 (当前步, 总步数, 本步毫秒, 累计秒)。</summary>
        public Action<int, int, double, double>? StepCallback { get; set; }

        /// <summary>
        /// 构造汇流求解器。
        /// </summary>
        /// <param name="trib1Solver">支流1初算求解器（已配置正常水深下游边界）。</param>
        /// <param name="trib2Solver">支流2初算求解器（已配置正常水深下游边界）。</param>
        /// <param name="mainSolverFactory">
        ///   干流（下游段）求解器工厂：接受合并入流过程线，返回初始化好的求解器。
        /// </param>
        /// <param name="upstreamMainSolver">
        ///   （可选）干流上游段初算求解器，中游汇流场景使用。
        /// </param>
        /// <param name="trib1RefinedFactory">
        ///   （可选）支流1精算工厂：接受下游水位边界，返回新的支流1求解器。
        ///   提供后将执行精算迭代，以汇流点水位作为支流1的下游边界。
        /// </param>
        /// <param name="trib2RefinedFactory">
        ///   （可选）支流2精算工厂，同上。
        /// </param>
        /// <param name="upMainRefinedFactory">
        ///   （可选）干流上游段精算工厂，中游汇流场景同上。
        /// </param>
        public JunctionSolver(
            Solver trib1Solver,
            Solver trib2Solver,
            Func<Hydrograph, Solver> mainSolverFactory,
            Solver? upstreamMainSolver = null,
            Func<Boundary, Solver>? trib1RefinedFactory = null,
            Func<Boundary, Solver>? trib2RefinedFactory = null,
            Func<Boundary, Solver>? upMainRefinedFactory = null)
        {
            _trib1Solver            = trib1Solver;
            _trib2Solver            = trib2Solver;
            _mainSolverFactory      = mainSolverFactory;
            _upstreamMainSolver     = upstreamMainSolver;
            _trib1RefinedFactory    = trib1RefinedFactory;
            _trib2RefinedFactory    = trib2RefinedFactory;
            _upMainRefinedFactory   = upMainRefinedFactory;
        }

        // ── 公开方法 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 执行顺序仿真。
        /// 若提供了精算工厂，则在初算完成后执行一次精算迭代，以改善汇流点水位的物理一致性。
        /// </summary>
        /// <param name="verbose">日志详细程度（传递给各子求解器）。</param>
        public void Run(int verbose = 0)
        {
            bool willRefine = _trib1RefinedFactory != null
                           || _trib2RefinedFactory != null
                           || _upMainRefinedFactory != null;

            // ─── 初算 ──────────────────────────────────────────────────────
            string pass = willRefine ? "（初算）" : "";

            LogCallback?.Invoke($"开始计算支流1{pass}…");
            _trib1Solver.StepCallback = StepCallback;
            _trib1Solver.Run(verbose);

            LogCallback?.Invoke($"开始计算支流2{pass}…");
            _trib2Solver.StepCallback = StepCallback;
            _trib2Solver.Run(verbose);

            if (_upstreamMainSolver != null)
            {
                LogCallback?.Invoke($"开始计算干流上游段{pass}…");
                _upstreamMainSolver.StepCallback = StepCallback;
                _upstreamMainSolver.Run(verbose);
            }

            ValidateTimeSteps();

            LogCallback?.Invoke($"汇合上游各入流，构造干流入流过程线{pass}…");
            var combinedHydro = BuildCombinedHydrograph(_trib1Solver, _trib2Solver, _upstreamMainSolver);

            LogCallback?.Invoke($"开始计算干流（下游段）{pass}…");
            MainSolver = _mainSolverFactory(combinedHydro);
            MainSolver.StepCallback = StepCallback;
            MainSolver.Run(verbose);

            // 记录初算汇流点水位
            ExtractJunctionStage();

            // ─── 精算（可选）──────────────────────────────────────────────
            if (willRefine)
                RunRefinedPass(verbose);
        }

        // ── 私有方法 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 验证各上游求解器时间步长一致，防止流量时序错位。
        /// </summary>
        private void ValidateTimeSteps()
        {
            double dt = _trib1Solver.TimeStep;

            void Check(Solver s, string name)
            {
                if (Math.Abs(s.TimeStep - dt) > TimeStepRelTol * dt)
                    throw new InvalidOperationException(
                        $"各上游求解器时间步长不一致，无法正确合并汇流流量。\n" +
                        $"  支流1 dt = {dt} s，{name} dt = {s.TimeStep} s。\n" +
                        $"  请确保支流1、支流2和干流上游段使用相同的时间步长。");
            }

            Check(_trib2Solver, "支流2");
            if (_upstreamMainSolver != null)
                Check(_upstreamMainSolver, "干流上游段");
        }

        /// <summary>
        /// 精算阶段：以初算得到的汇流点水位作为各上游段的下游水位边界，重新求解后再次求解干流。
        /// </summary>
        private void RunRefinedPass(int verbose)
        {
            int    nk = JunctionStage!.Length;
            double dt = _trib1Solver.TimeStep;

            double minStage = JunctionStage[0], maxStage = JunctionStage[0];
            for (int k = 1; k < nk; k++)
            {
                if (JunctionStage[k] < minStage) minStage = JunctionStage[k];
                if (JunctionStage[k] > maxStage) maxStage = JunctionStage[k];
            }
            LogCallback?.Invoke(
                $"汇流点初算水位：{minStage:F3}～{maxStage:F3} m，开始精算迭代…");

            // ── 精算支流1 ──
            if (_trib1RefinedFactory != null)
            {
                var stageBC = BuildJunctionStageBC(_trib1Solver, JunctionStage, dt, nk);
                LogCallback?.Invoke("精算：重新求解支流1（汇流点水位下游边界）…");
                _trib1Solver = _trib1RefinedFactory(stageBC);
                _trib1Solver.StepCallback = StepCallback;
                _trib1Solver.Run(verbose);
            }

            // ── 精算支流2 ──
            if (_trib2RefinedFactory != null)
            {
                var stageBC = BuildJunctionStageBC(_trib2Solver, JunctionStage, dt, nk);
                LogCallback?.Invoke("精算：重新求解支流2（汇流点水位下游边界）…");
                _trib2Solver = _trib2RefinedFactory(stageBC);
                _trib2Solver.StepCallback = StepCallback;
                _trib2Solver.Run(verbose);
            }

            // ── 精算干流上游段 ──
            if (_upMainRefinedFactory != null && _upstreamMainSolver != null)
            {
                var stageBC = BuildJunctionStageBC(_upstreamMainSolver, JunctionStage, dt, nk);
                LogCallback?.Invoke("精算：重新求解干流上游段（汇流点水位下游边界）…");
                _upstreamMainSolver = _upMainRefinedFactory(stageBC);
                _upstreamMainSolver.StepCallback = StepCallback;
                _upstreamMainSolver.Run(verbose);
            }

            // ── 重新合并流量，再次求解干流 ──
            LogCallback?.Invoke("精算：重新汇合上游出口流量…");
            var refinedCombined = BuildCombinedHydrograph(_trib1Solver, _trib2Solver, _upstreamMainSolver);

            LogCallback?.Invoke("精算：重新求解干流（下游段）…");
            MainSolver = _mainSolverFactory(refinedCombined);
            MainSolver.StepCallback = StepCallback;
            MainSolver.Run(verbose);

            // 更新为精算后的汇流点水位
            ExtractJunctionStage();
            IsRefined = true;

            double minR = JunctionStage[0], maxR = JunctionStage[0];
            for (int k = 1; k < JunctionStage.Length; k++)
            {
                if (JunctionStage[k] < minR) minR = JunctionStage[k];
                if (JunctionStage[k] > maxR) maxR = JunctionStage[k];
            }
            LogCallback?.Invoke(
                $"精算完成。汇流点精算水位：{minR:F3}～{maxR:F3} m。");
        }

        /// <summary>
        /// 从干流下游段首节点提取汇流点水位，写入 <see cref="JunctionStage"/>。
        /// </summary>
        private void ExtractJunctionStage()
        {
            int nk = MainSolver!.TimeLevel + 1;
            JunctionStage = new double[nk];
            for (int k = 0; k < nk; k++)
                JunctionStage[k] = MainSolver.Level![k, 0];
        }

        /// <summary>
        /// 为指定上游求解器构造"汇流点水位"下游边界条件（<see cref="BoundaryConditionType.StageHydrograph"/>）。
        /// <para>
        /// 水位过程线以水深（= 汇流点绝对水位 − 该上游段出口床底高程）存储，
        /// <see cref="BoundaryConditionType.StageHydrograph"/> 残差中令 BedLevel = 0，
        /// 则 target = 水深值，直接约束出口水深等于汇流点对应水深。
        /// </para>
        /// </summary>
        /// <param name="upstreamSolver">上游段求解器（已完成初算，BedProfile 已填充）。</param>
        /// <param name="junctionStage">汇流点水位过程线（绝对水位，m）。</param>
        /// <param name="dt">时间步长（s）。</param>
        /// <param name="nk">时间步数量。</param>
        private static Boundary BuildJunctionStageBC(
            Solver upstreamSolver, double[] junctionStage, double dt, int nk)
        {
            int    lastNode    = upstreamSolver.NumberOfNodes - 1;
            double bedAtOutlet = upstreamSolver.BedProfile![lastNode];

            // 构造水深过程线：depth[k] = junctionStage[k] − bedAtOutlet
            // 对极端情况做物理下限保护（不小于 1 mm），防止求解器因水深为零而发散
            int nkActual = Math.Min(nk, upstreamSolver.TimeLevel + 1);
            var depthTable = new double[nkActual, 2];
            for (int k = 0; k < nkActual; k++)
            {
                double depth = junctionStage[k] - bedAtOutlet;
                if (depth < MinimumOutletDepth) depth = MinimumOutletDepth;
                depthTable[k, 0] = k * dt;
                depthTable[k, 1] = depth;
            }
            double initialDepth = depthTable[0, 1];

            // BedLevel = 0：残差 target = hydrograph.GetAt(t) − 0 = depth，
            // 即直接约束出口水深等于汇流点水深
            return new Boundary(
                BoundaryConditionType.StageHydrograph,
                chainage: 0,
                bedLevel: 0,
                initialDepth: initialDepth,
                hydrograph: new Hydrograph(table: depthTable));
        }

        /// <summary>
        /// 将各上游段出口流量按时步对齐求和，构造干流下游段的上游流量过程线。
        /// </summary>
        private static Hydrograph BuildCombinedHydrograph(
            Solver trib1, Solver trib2, Solver? upMain)
        {
            int nk = Math.Min(trib1.TimeLevel + 1, trib2.TimeLevel + 1);
            if (upMain != null)
                nk = Math.Min(nk, upMain.TimeLevel + 1);

            double dt  = trib1.TimeStep;
            int    nn1 = trib1.NumberOfNodes;
            int    nn2 = trib2.NumberOfNodes;
            int    nnUp = upMain?.NumberOfNodes ?? 0;

            var table = new double[nk, 2];
            for (int k = 0; k < nk; k++)
            {
                double q1  = trib1.Flow![k, nn1 - 1];
                double q2  = trib2.Flow![k, nn2 - 1];
                double qUp = upMain != null ? upMain.Flow![k, nnUp - 1] : 0.0;
                table[k, 0] = k * dt;
                table[k, 1] = q1 + q2 + qUp;
            }
            return new Hydrograph(table: table);
        }
    }
}
