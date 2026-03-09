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

        // ── 中游段工厂（可选，仅三段式汇流使用）──
        // 当支流1与支流2的汇流点位于干流的不同桩号时，干流被划分为三段：
        //   上游段 → [支流1汇口] → 中游段 → [支流2汇口] → 下游段
        // 此工厂接受中游段的上游流量过程线，返回新的中游段求解器。
        private readonly Func<Hydrograph, Solver>? _midMainSolverFactory;

        // ── 精算工厂（可选）：接受下游水位边界，返回新的求解器实例 ──
        private readonly Func<Boundary, Solver>? _trib1RefinedFactory;
        private readonly Func<Boundary, Solver>? _trib2RefinedFactory;
        private readonly Func<Boundary, Solver>? _upMainRefinedFactory;
        private readonly Func<Boundary, Solver>? _midMainRefinedFactory;

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

        /// <summary>中游段求解器（仅三段式汇流有效；精算后返回精算版本）。</summary>
        public Solver? MidMainSolver { get; private set; }

        /// <summary>
        /// 第一汇流点（支流1汇口）水位过程线（m），仅三段式汇流有效。
        /// 单汇口模式时为 null；精算后为精算水位。
        /// </summary>
        public double[]? FirstJunctionStage { get; private set; }

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
        /// <param name="midMainSolverFactory">
        ///   （可选）干流中游段初算工厂（三段式汇流）：接受第一汇口的合并入流，返回中游段求解器。
        ///   提供此参数时，支流1和干流上游段在第一汇口汇合，再通过中游段流向第二汇口，
        ///   支流2在第二汇口汇入，最终进入下游段。
        /// </param>
        /// <param name="midMainRefinedFactory">
        ///   （可选）干流中游段精算工厂，接受第二汇口水位边界，返回精算中游段求解器。
        /// </param>
        public JunctionSolver(
            Solver trib1Solver,
            Solver trib2Solver,
            Func<Hydrograph, Solver> mainSolverFactory,
            Solver? upstreamMainSolver = null,
            Func<Boundary, Solver>? trib1RefinedFactory = null,
            Func<Boundary, Solver>? trib2RefinedFactory = null,
            Func<Boundary, Solver>? upMainRefinedFactory = null,
            Func<Hydrograph, Solver>? midMainSolverFactory = null,
            Func<Boundary, Solver>? midMainRefinedFactory = null)
        {
            _trib1Solver            = trib1Solver;
            _trib2Solver            = trib2Solver;
            _mainSolverFactory      = mainSolverFactory;
            _upstreamMainSolver     = upstreamMainSolver;
            _trib1RefinedFactory    = trib1RefinedFactory;
            _trib2RefinedFactory    = trib2RefinedFactory;
            _upMainRefinedFactory   = upMainRefinedFactory;
            _midMainSolverFactory   = midMainSolverFactory;
            _midMainRefinedFactory  = midMainRefinedFactory;
        }

        // ── 公开方法 ──────────────────────────────────────────────────────────

        /// <summary>
        /// 执行顺序仿真。
        /// 若提供了精算工厂，则在初算完成后执行一次精算迭代，以改善汇流点水位的物理一致性。
        /// 若提供了 <see cref="_midMainSolverFactory"/>，则按三段式算法执行：
        ///   上游段 + 支流1 → 汇口1 → 中游段 + 支流2 → 汇口2 → 下游段。
        /// </summary>
        /// <param name="verbose">日志详细程度（传递给各子求解器）。</param>
        public void Run(int verbose = 0)
        {
            bool willRefine = _trib1RefinedFactory != null
                           || _trib2RefinedFactory != null
                           || _upMainRefinedFactory != null
                           || _midMainRefinedFactory != null;

            string pass = willRefine ? "（初算）" : "";

            if (_midMainSolverFactory != null)
            {
                // ─── 三段式初算 ────────────────────────────────────────────
                RunThreeSegmentPass(verbose, pass);
            }
            else
            {
                // ─── 两段式初算（原有逻辑）──────────────────────────────────
                RunTwoSegmentPass(verbose, pass);
            }

            // ─── 精算（可选）──────────────────────────────────────────────
            if (willRefine)
                RunRefinedPass(verbose);
        }

        // ── 私有方法 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 两段式初算（两支流在同一汇口汇入干流）。
        /// </summary>
        private void RunTwoSegmentPass(int verbose, string pass)
        {
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

            // 记录汇流点水位
            ExtractJunctionStage();
        }

        /// <summary>
        /// 三段式初算（支流1在第一汇口汇入，支流2在第二汇口汇入，干流分三段）。
        /// 上游段 + 支流1 → 汇口1 → 中游段 → 汇口2（+ 支流2） → 下游段。
        /// </summary>
        private void RunThreeSegmentPass(int verbose, string pass)
        {
            // 第一汇口上游各支流
            LogCallback?.Invoke($"开始计算支流1（第一汇口）{pass}…");
            _trib1Solver.StepCallback = StepCallback;
            _trib1Solver.Run(verbose);

            if (_upstreamMainSolver != null)
            {
                LogCallback?.Invoke($"开始计算干流上游段{pass}…");
                _upstreamMainSolver.StepCallback = StepCallback;
                _upstreamMainSolver.Run(verbose);
            }

            ValidateTimeSteps();

            // 第一汇口：合并支流1 + 上游干流出口流量
            LogCallback?.Invoke($"汇口1：合并支流1与干流上游段出口流量{pass}…");
            var hydro1 = BuildCombinedHydrograph(_trib1Solver, null, _upstreamMainSolver);

            // 求解中游段
            LogCallback?.Invoke($"开始计算干流（中游段）{pass}…");
            MidMainSolver = _midMainSolverFactory!(hydro1);
            MidMainSolver.StepCallback = StepCallback;
            MidMainSolver.Run(verbose);

            // 记录第一汇流点水位（中游段首节点水位）
            ExtractFirstJunctionStage();

            // 第二汇口上游：支流2
            LogCallback?.Invoke($"开始计算支流2（第二汇口）{pass}…");
            _trib2Solver.StepCallback = StepCallback;
            _trib2Solver.Run(verbose);

            // 时间步一致性（支流2 vs 支流1）
            if (Math.Abs(_trib2Solver.TimeStep - _trib1Solver.TimeStep) > TimeStepRelTol * _trib1Solver.TimeStep)
                throw new InvalidOperationException(
                    $"支流2的时间步长（{_trib2Solver.TimeStep} s）与支流1（{_trib1Solver.TimeStep} s）不一致。");

            // 第二汇口：合并中游段出口 + 支流2出口
            LogCallback?.Invoke($"汇口2：合并中游段与支流2出口流量{pass}…");
            var hydro2 = BuildCombinedHydrograph(MidMainSolver, _trib2Solver, null);

            // 求解下游段
            LogCallback?.Invoke($"开始计算干流（下游段）{pass}…");
            MainSolver = _mainSolverFactory(hydro2);
            MainSolver.StepCallback = StepCallback;
            MainSolver.Run(verbose);

            // 记录第二汇流点水位（下游段首节点水位）
            ExtractJunctionStage();
        }


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

            // 三段式模式中支流2的一致性检查在 RunThreeSegmentPass 中单独执行；
            // 两段式模式中需要检查所有支流。
            if (_midMainSolverFactory == null)
                Check(_trib2Solver, "支流2");
            if (_upstreamMainSolver != null)
                Check(_upstreamMainSolver, "干流上游段");
        }

        /// <summary>
        /// 精算阶段：以初算得到的汇流点水位作为各上游段的下游水位边界，重新求解后再次求解干流。
        /// 支持两段式和三段式汇流。
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

            if (_midMainSolverFactory != null)
            {
                // ─── 三段式精算 ────────────────────────────────────────────
                // 第一汇口水位 = 中游段首节点水位
                var firstStage = FirstJunctionStage!;

                // 精算支流1（使用第一汇口水位作为下游边界）
                if (_trib1RefinedFactory != null)
                {
                    var stageBC = BuildJunctionStageBC(_trib1Solver, firstStage, dt, nk);
                    LogCallback?.Invoke("精算：重新求解支流1（第一汇口水位下游边界）…");
                    _trib1Solver = _trib1RefinedFactory(stageBC);
                    _trib1Solver.StepCallback = StepCallback;
                    _trib1Solver.Run(verbose);
                }

                // 精算干流上游段（使用第一汇口水位作为下游边界）
                if (_upMainRefinedFactory != null && _upstreamMainSolver != null)
                {
                    var stageBC = BuildJunctionStageBC(_upstreamMainSolver, firstStage, dt, nk);
                    LogCallback?.Invoke("精算：重新求解干流上游段（第一汇口水位下游边界）…");
                    _upstreamMainSolver = _upMainRefinedFactory(stageBC);
                    _upstreamMainSolver.StepCallback = StepCallback;
                    _upstreamMainSolver.Run(verbose);
                }

                // 重新汇合第一汇口流量，精算中游段
                LogCallback?.Invoke("精算：重新汇合汇口1流量…");
                var hydro1R = BuildCombinedHydrograph(_trib1Solver, null, _upstreamMainSolver);

                // 精算中游段（使用第二汇口水位作为下游边界）
                if (_midMainRefinedFactory != null)
                {
                    var stageBC = BuildJunctionStageBC(MidMainSolver!, JunctionStage, dt, nk);
                    LogCallback?.Invoke("精算：重新求解干流中游段（第二汇口水位下游边界）…");
                    MidMainSolver = _midMainRefinedFactory(stageBC);
                    MidMainSolver.StepCallback = StepCallback;
                    MidMainSolver.Run(verbose);
                }
                else
                {
                    LogCallback?.Invoke("精算：重新求解干流（中游段）…");
                    MidMainSolver = _midMainSolverFactory!(hydro1R);
                    MidMainSolver.StepCallback = StepCallback;
                    MidMainSolver.Run(verbose);
                }

                // 更新第一汇口水位
                ExtractFirstJunctionStage();

                // 精算支流2（使用第二汇口水位作为下游边界）
                if (_trib2RefinedFactory != null)
                {
                    var stageBC = BuildJunctionStageBC(_trib2Solver, JunctionStage, dt, nk);
                    LogCallback?.Invoke("精算：重新求解支流2（第二汇口水位下游边界）…");
                    _trib2Solver = _trib2RefinedFactory(stageBC);
                    _trib2Solver.StepCallback = StepCallback;
                    _trib2Solver.Run(verbose);
                }

                // 重新汇合第二汇口流量，精算下游段
                LogCallback?.Invoke("精算：重新汇合汇口2流量…");
                var hydro2R = BuildCombinedHydrograph(MidMainSolver, _trib2Solver, null);

                LogCallback?.Invoke("精算：重新求解干流（下游段）…");
                MainSolver = _mainSolverFactory(hydro2R);
                MainSolver.StepCallback = StepCallback;
                MainSolver.Run(verbose);
            }
            else
            {
                // ─── 两段式精算（原有逻辑）──────────────────────────────────
                // 精算支流1
                if (_trib1RefinedFactory != null)
                {
                    var stageBC = BuildJunctionStageBC(_trib1Solver, JunctionStage, dt, nk);
                    LogCallback?.Invoke("精算：重新求解支流1（汇流点水位下游边界）…");
                    _trib1Solver = _trib1RefinedFactory(stageBC);
                    _trib1Solver.StepCallback = StepCallback;
                    _trib1Solver.Run(verbose);
                }

                // 精算支流2
                if (_trib2RefinedFactory != null)
                {
                    var stageBC = BuildJunctionStageBC(_trib2Solver, JunctionStage, dt, nk);
                    LogCallback?.Invoke("精算：重新求解支流2（汇流点水位下游边界）…");
                    _trib2Solver = _trib2RefinedFactory(stageBC);
                    _trib2Solver.StepCallback = StepCallback;
                    _trib2Solver.Run(verbose);
                }

                // 精算干流上游段
                if (_upMainRefinedFactory != null && _upstreamMainSolver != null)
                {
                    var stageBC = BuildJunctionStageBC(_upstreamMainSolver, JunctionStage, dt, nk);
                    LogCallback?.Invoke("精算：重新求解干流上游段（汇流点水位下游边界）…");
                    _upstreamMainSolver = _upMainRefinedFactory(stageBC);
                    _upstreamMainSolver.StepCallback = StepCallback;
                    _upstreamMainSolver.Run(verbose);
                }

                // 重新合并流量，再次求解干流
                LogCallback?.Invoke("精算：重新汇合上游出口流量…");
                var refinedCombined = BuildCombinedHydrograph(_trib1Solver, _trib2Solver, _upstreamMainSolver);

                LogCallback?.Invoke("精算：重新求解干流（下游段）…");
                MainSolver = _mainSolverFactory(refinedCombined);
                MainSolver.StepCallback = StepCallback;
                MainSolver.Run(verbose);
            }

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
        /// 从干流中游段首节点提取第一汇流点水位，写入 <see cref="FirstJunctionStage"/>（三段式专用）。
        /// </summary>
        private void ExtractFirstJunctionStage()
        {
            if (MidMainSolver == null) return;
            int nk = MidMainSolver.TimeLevel + 1;
            FirstJunctionStage = new double[nk];
            for (int k = 0; k < nk; k++)
                FirstJunctionStage[k] = MidMainSolver.Level![k, 0];
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
        /// <paramref name="trib2"/> 可为 null（三段式汇流中第一汇口只有支流1）。
        /// </summary>
        private static Hydrograph BuildCombinedHydrograph(
            Solver trib1, Solver? trib2, Solver? upMain)
        {
            int nk = trib1.TimeLevel + 1;
            if (trib2 != null) nk = Math.Min(nk, trib2.TimeLevel + 1);
            if (upMain != null) nk = Math.Min(nk, upMain.TimeLevel + 1);

            double dt  = trib1.TimeStep;
            int    nn1 = trib1.NumberOfNodes;
            int    nn2 = trib2?.NumberOfNodes ?? 0;
            int    nnUp = upMain?.NumberOfNodes ?? 0;

            var table = new double[nk, 2];
            for (int k = 0; k < nk; k++)
            {
                double q1  = trib1.Flow![k, nn1 - 1];
                double q2  = trib2 != null ? trib2.Flow![k, nn2 - 1] : 0.0;
                double qUp = upMain != null ? upMain.Flow![k, nnUp - 1] : 0.0;
                table[k, 0] = k * dt;
                table[k, 1] = q1 + q2 + qUp;
            }
            return new Hydrograph(table: table);
        }
    }
}
