using System;

namespace FlowSim.Models
{
    /// <summary>
    /// 两支流汇合到一条干流的顺序（非耦合）求解器。
    /// <para>
    /// 算法流程（顺序非耦合法）：
    /// <list type="number">
    ///   <item>独立求解支流1（使用各自的上游入流过程线）；</item>
    ///   <item>独立求解支流2；</item>
    ///   <item>将两支流出口处的流量按时步对齐求和，构造干流上游入流过程线；</item>
    ///   <item>以合并后的过程线为干流上游边界，求解干流。</item>
    /// </list>
    /// 此方法为"非耦合"法，忽略了汇口处水位连续条件对支流的反馈影响，
    /// 适用于概念展示、教学用途或支流流量较小的工程估算场景。
    /// </para>
    /// </summary>
    public class JunctionSolver
    {
        private readonly Solver _trib1Solver;
        private readonly Solver _trib2Solver;

        /// <summary>
        /// 干流求解器工厂函数：接受合并后的上游入流过程线，返回初始化好的干流求解器实例。
        /// 仿真完成后通过 <see cref="MainSolver"/> 属性访问干流求解结果。
        /// </summary>
        private readonly Func<Hydrograph, Solver> _mainSolverFactory;

        /// <summary>支流1求解器（仅供外部读取结果用）。</summary>
        public Solver Tributary1Solver => _trib1Solver;

        /// <summary>支流2求解器（仅供外部读取结果用）。</summary>
        public Solver Tributary2Solver => _trib2Solver;

        /// <summary>
        /// 干流求解器实例（<see cref="Run"/> 执行后才创建并赋值，执行前为 null）。
        /// </summary>
        public Solver? MainSolver { get; private set; }

        /// <summary>三段仿真是否均已成功完成。</summary>
        public bool Solved => MainSolver?.Solved ?? false;

        /// <summary>阶段日志回调：每个阶段（支流1/支流2/干流）开始时调用，参数为阶段描述字符串。</summary>
        public Action<string>? LogCallback { get; set; }

        /// <summary>
        /// 每时步进度回调：转发给当前活跃的子求解器；参数为 (当前步, 总步数, 本步毫秒, 累计秒)。
        /// </summary>
        public Action<int, int, double, double>? StepCallback { get; set; }

        /// <summary>
        /// 构造汇流求解器。
        /// </summary>
        /// <param name="trib1Solver">支流1求解器（已用支流1参数初始化）。</param>
        /// <param name="trib2Solver">支流2求解器（已用支流2参数初始化）。</param>
        /// <param name="mainSolverFactory">
        ///   干流求解器工厂：接受合并入流 <see cref="Hydrograph"/>，返回初始化好的干流求解器。
        ///   在工厂内部应以该 Hydrograph 作为干流上游边界。
        /// </param>
        public JunctionSolver(
            Solver trib1Solver,
            Solver trib2Solver,
            Func<Hydrograph, Solver> mainSolverFactory)
        {
            _trib1Solver = trib1Solver;
            _trib2Solver = trib2Solver;
            _mainSolverFactory = mainSolverFactory;
        }

        /// <summary>
        /// 执行三段顺序仿真：支流1 → 支流2 → 汇流 → 干流。
        /// </summary>
        /// <param name="verbose">日志详细程度（传递给各子求解器）。</param>
        public void Run(int verbose = 0)
        {
            // ── 阶段1：求解支流1 ──
            LogCallback?.Invoke("开始计算支流1…");
            _trib1Solver.StepCallback = StepCallback;
            _trib1Solver.Run(verbose);

            // ── 阶段2：求解支流2 ──
            LogCallback?.Invoke("开始计算支流2…");
            _trib2Solver.StepCallback = StepCallback;
            _trib2Solver.Run(verbose);

            // ── 汇流：将两支流出口流量叠加，构造干流上游入流过程线 ──
            LogCallback?.Invoke("汇合两支流流量，构造干流入流过程线…");

            int nk1 = _trib1Solver.TimeLevel + 1;
            int nk2 = _trib2Solver.TimeLevel + 1;
            int nk  = Math.Min(nk1, nk2);
            double dt = _trib1Solver.TimeStep;
            int nn1 = _trib1Solver.NumberOfNodes;
            int nn2 = _trib2Solver.NumberOfNodes;

            // 构造 [nk, 2] 的时间-流量表：第 0 列时间(s)，第 1 列合并流量(m³/s)
            var table = new double[nk, 2];
            for (int k = 0; k < nk; k++)
            {
                double q1 = _trib1Solver.Flow![k, nn1 - 1];   // 支流1出口流量
                double q2 = _trib2Solver.Flow![k, nn2 - 1];   // 支流2出口流量
                table[k, 0] = k * dt;
                table[k, 1] = q1 + q2;
            }
            var combinedHydro = new Hydrograph(table: table);

            // ── 阶段3：求解干流 ──
            LogCallback?.Invoke("开始计算干流…");
            MainSolver = _mainSolverFactory(combinedHydro);
            MainSolver.StepCallback = StepCallback;
            MainSolver.Run(verbose);
        }
    }
}
