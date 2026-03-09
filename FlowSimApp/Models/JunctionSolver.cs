using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowSim.Models
{
    /// <summary>
    /// 多支流顺序汇流求解器，支持 N 条支流分别在干流不同桩号处汇入，
    /// 并提供一次精算迭代以改善汇流点水位的物理一致性。
    /// <para>
    /// 算法流程（初算 + 可选精算）：
    /// <list type="number">
    ///   <item>【初算】独立求解所有支流（及可选的干流上游段），下游边界均取正常水深；</item>
    ///   <item>【初算】按汇流桩号从上游到下游依次汇合各支流，每一汇口产生一段干流段；</item>
    ///   <item>【精算】以各汇口初算水位作为对应支流的下游水位边界，重新求解各支流；</item>
    ///   <item>【精算】顺序重新求解各干流段，使汇流点水位物理一致性更优。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 支持场景举例：
    /// <list type="bullet">
    ///   <item>N=1：单支流汇入，干流分两段（可选上游段 + 下游段）；</item>
    ///   <item>N=2，同一桩号：两支流在同一位置汇入，干流分两段；</item>
    ///   <item>N=2，不同桩号：两支流先后汇入，干流分三段；</item>
    ///   <item>N≥3：多支流逐段汇入，干流分 N+1 段（含可选上游段）。</item>
    /// </list>
    /// </para>
    /// </summary>
    public class JunctionSolver
    {
        // ── 支流列表（按汇流桩号升序排列）──
        private readonly TributaryEntry[] _tributaries;

        // ── 干流上游段（第一汇口之前，可选）──
        private Solver? _upstreamMainSolver;
        private readonly Func<Boundary, Solver>? _upMainRefinedFactory;

        // ── 干流各段工厂（N 个，第 i 个创建第 i 个汇口之后的干流段）──
        // segmentFactories[i] 对应第 i 个汇口下游的干流段（接受合并入流，返回求解器）。
        private readonly Func<Hydrograph, Solver>[] _segmentFactories;

        // ── 中间段精算工厂（N-1 个，仅针对非最终段）──
        // refinedSegFactories[i] 对应第 i 段的精算版本（接受第 i+1 汇口水位边界）。
        // 最终下游段不需要精算工厂，因为其下游边界由用户设定，不依赖更下游的汇口水位。
        private readonly Func<Boundary, Solver>?[] _refinedSegFactories;

        // ── 当前活跃的干流各段求解器（精算后会替换为精算版本）──
        private Solver[] _segmentSolvers;

        /// <summary>汇流点水位边界中允许的最小出口水深（m）。</summary>
        private const double MinimumOutletDepth = 0.001;   // 1 mm

        /// <summary>时间步长一致性校验的相对容差。</summary>
        private const double TimeStepRelTol = 1e-6;

        /// <summary>
        /// 汇流桩号差值不超过此阈值（m）的支流视为位于同一汇口，合并处理。
        /// </summary>
        private const double MinJunctionSeparationM = 1.0;

        // ──────────────────────────────────────────────────────────────────────
        // 公开属性
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>各支流求解器（精算完成后为精算版本），按汇流桩号升序排列。</summary>
        public IReadOnlyList<Solver> TributarySolvers =>
            Array.ConvertAll(_tributaries, t => t.Solver);

        /// <summary>
        /// 第一条支流的求解器（按汇流桩号）。
        /// 提供此便捷属性以保持向后兼容。
        /// </summary>
        public Solver Tributary1Solver => _tributaries[0].Solver;

        /// <summary>
        /// 第二条支流的求解器（按汇流桩号）。
        /// 仅在 N≥2 时有效；提供此便捷属性以保持向后兼容。
        /// </summary>
        public Solver Tributary2Solver => _tributaries.Length >= 2 ? _tributaries[1].Solver
            : throw new InvalidOperationException("汇流求解器中支流数量不足 2 条。");

        /// <summary>干流各段求解器（按汇流桩号升序），精算后为精算版本。</summary>
        public IReadOnlyList<Solver?> SegmentSolvers => _segmentSolvers;

        /// <summary>干流上游段求解器（精算完成后为精算版本；无上游段时为 null）。</summary>
        public Solver? UpstreamMainSolver => _upstreamMainSolver;

        /// <summary>干流最终下游段求解器，即 <see cref="Run"/> 执行后最后一段的求解器。</summary>
        public Solver? MainSolver => _segmentSolvers.Length > 0 ? _segmentSolvers[^1] : null;

        /// <summary>仿真是否成功完成。</summary>
        public bool Solved => MainSolver?.Solved ?? false;

        /// <summary>
        /// 各汇口绝对水位过程线（m）。
        /// JunctionStages[i] 是第 i 个汇口的水位过程线（干流第 i 段首节点水位）。
        /// </summary>
        public double[]?[] JunctionStages { get; private set; }

        /// <summary>
        /// 最终汇流点（最下游汇口）的绝对水位过程线（m）。
        /// 向后兼容属性，等价于 <c>JunctionStages[^1]</c>。
        /// </summary>
        public double[]? JunctionStage => JunctionStages.Length > 0 ? JunctionStages[^1] : null;

        /// <summary>
        /// 第一汇口的水位过程线（m）。
        /// 仅在 N≥2 时有意义；向后兼容属性，等价于 <c>JunctionStages[0]</c>。
        /// </summary>
        public double[]? FirstJunctionStage => JunctionStages.Length > 0 ? JunctionStages[0] : null;

        /// <summary>是否已完成精算迭代。</summary>
        public bool IsRefined { get; private set; }

        /// <summary>阶段日志回调。</summary>
        public Action<string>? LogCallback { get; set; }

        /// <summary>每时步进度回调，参数为 (当前步, 总步数, 本步毫秒, 累计秒)。</summary>
        public Action<int, int, double, double>? StepCallback { get; set; }

        // ──────────────────────────────────────────────────────────────────────
        // 构造函数
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 构造多支流汇流求解器。
        /// </summary>
        /// <param name="tributaries">
        ///   各支流描述（<see cref="TributaryEntry"/>），含初算求解器、汇流桩号和可选精算工厂。
        ///   内部会按 <see cref="TributaryEntry.JunctionChainageM"/> 升序排序。
        ///   至少需要 1 条支流。
        /// </param>
        /// <param name="segmentFactories">
        ///   干流各段工厂，共 N 个（N = <paramref name="tributaries"/> 中不重复汇流桩号的数量）。
        ///   <c>segmentFactories[i]</c> 接受第 i 个汇口处的合并入流过程线，返回该汇口下游干流段求解器。
        /// </param>
        /// <param name="upstreamMainSolver">
        ///   （可选）干流上游段初算求解器（第一汇口之前的干流段）。
        /// </param>
        /// <param name="upMainRefinedFactory">
        ///   （可选）干流上游段精算工厂：接受第一汇口水位边界，返回精算干流上游段求解器。
        /// </param>
        /// <param name="refinedSegmentFactories">
        ///   （可选）中间干流段精算工厂，长度应为 N-1（最终下游段不需要精算工厂）。
        ///   <c>refinedSegmentFactories[i]</c> 接受第 i+1 汇口水位边界，返回精算第 i 段求解器。
        ///   可为 <see langword="null"/> 或其中某项为 <see langword="null"/> 以跳过对应段的精算。
        /// </param>
        public JunctionSolver(
            IEnumerable<TributaryEntry>                    tributaries,
            IEnumerable<Func<Hydrograph, Solver>>          segmentFactories,
            Solver?                                        upstreamMainSolver       = null,
            Func<Boundary, Solver>?                        upMainRefinedFactory     = null,
            IEnumerable<Func<Boundary, Solver>?>?          refinedSegmentFactories  = null)
        {
            if (tributaries == null) throw new ArgumentNullException(nameof(tributaries));
            if (segmentFactories == null) throw new ArgumentNullException(nameof(segmentFactories));

            // 按汇流桩号升序排列支流
            _tributaries = tributaries.ToArray();
            if (_tributaries.Length == 0)
                throw new ArgumentException("至少需要一条支流。", nameof(tributaries));
            // 使用稳定排序（LINQ OrderBy），确保桩号相同时保持原始输入顺序，
            // 以便调用方能通过原始顺序（索引）正确识别各支流的结果。
            _tributaries = tributaries.OrderBy(t => t.JunctionChainageM).ToArray();

            _segmentFactories    = segmentFactories.ToArray();
            _upstreamMainSolver  = upstreamMainSolver;
            _upMainRefinedFactory = upMainRefinedFactory;

            int nFactories = _segmentFactories.Length;
            _refinedSegFactories = refinedSegmentFactories != null
                ? refinedSegmentFactories.ToArray()
                : new Func<Boundary, Solver>?[Math.Max(0, nFactories - 1)];

            _segmentSolvers = new Solver[nFactories];
            JunctionStages  = new double[]?[nFactories];
        }

        // ──────────────────────────────────────────────────────────────────────
        // 公开方法
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 执行顺序仿真。
        /// <list type="number">
        ///   <item>初算：独立求解各支流和干流上游段，然后依次汇合并求解各干流段；</item>
        ///   <item>精算（可选）：若任意支流或干流段提供了精算工厂，则执行一次精算迭代。</item>
        /// </list>
        /// </summary>
        /// <param name="verbose">日志详细程度（传递给各子求解器）。</param>
        public void Run(int verbose = 0)
        {
            bool willRefine = _upMainRefinedFactory != null
                || _tributaries.Any(t => t.RefinedFactory != null)
                || _refinedSegFactories.Any(f => f != null);

            string passLabel = willRefine ? "（初算）" : "";

            RunMainPass(verbose, passLabel);

            if (willRefine)
                RunRefinedPass(verbose);
        }

        // ──────────────────────────────────────────────────────────────────────
        // 初算
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 主算法流程：求解所有支流和干流上游段，然后从第一汇口到最后汇口依次求解各干流段。
        /// </summary>
        private void RunMainPass(int verbose, string passLabel)
        {
            // ① 独立求解各支流
            for (int i = 0; i < _tributaries.Length; i++)
            {
                LogCallback?.Invoke($"开始计算支流 {i + 1}{passLabel}（汇口桩号 {_tributaries[i].JunctionChainageM / 1000.0:F2} km）…");
                _tributaries[i].Solver.StepCallback = StepCallback;
                _tributaries[i].Solver.Run(verbose);
            }

            // ② 独立求解干流上游段（可选）
            if (_upstreamMainSolver != null)
            {
                LogCallback?.Invoke($"开始计算干流上游段{passLabel}…");
                _upstreamMainSolver.StepCallback = StepCallback;
                _upstreamMainSolver.Run(verbose);
            }

            ValidateTimeSteps();

            // ③ 按汇口从上游到下游依次汇合，求解各干流段
            // 使用"逻辑汇口"：将相邻桩号差 ≤ MinJunctionSeparationM 的支流视为同一汇口
            RunSegments(verbose, passLabel, isRefinedPass: false);
        }

        // ──────────────────────────────────────────────────────────────────────
        // 精算
        // ──────────────────────────────────────────────────────────────────────

        private void RunRefinedPass(int verbose)
        {
            double dt = _tributaries[0].Solver.TimeStep;
            int    nk = JunctionStage!.Length;

            LogStageRange("汇流点初算水位", JunctionStage);
            LogCallback?.Invoke("开始精算迭代…");

            // ① 精算各支流（以对应汇口水位作为下游水位边界）
            // 多个支流共享同一汇口时，使用相同的汇口水位边界
            for (int i = 0; i < _tributaries.Length; i++)
            {
                // 确定本支流对应的汇口段索引（段索引 = 距离排序后的汇口序号）
                int mySegIdx = GetSegmentIndexForTrib(i);
                double[]? myJunctionStage = JunctionStages[mySegIdx];

                if (_tributaries[i].RefinedFactory != null && myJunctionStage != null)
                {
                    var stageBC = BuildJunctionStageBC(_tributaries[i].Solver, myJunctionStage, dt, nk);
                    LogCallback?.Invoke($"精算：重新求解支流 {i + 1}（汇口水位下游边界）…");
                    _tributaries[i].Solver = _tributaries[i].RefinedFactory!(stageBC);
                    _tributaries[i].Solver.StepCallback = StepCallback;
                    _tributaries[i].Solver.Run(verbose);
                }
            }

            // ② 精算干流上游段（以第一汇口水位作为下游水位边界）
            if (_upMainRefinedFactory != null && _upstreamMainSolver != null && JunctionStages[0] != null)
            {
                var stageBC = BuildJunctionStageBC(_upstreamMainSolver, JunctionStages[0]!, dt, nk);
                LogCallback?.Invoke("精算：重新求解干流上游段（第一汇口水位下游边界）…");
                _upstreamMainSolver = _upMainRefinedFactory(stageBC);
                _upstreamMainSolver.StepCallback = StepCallback;
                _upstreamMainSolver.Run(verbose);
            }

            // ③ 顺序重新求解各干流段
            RunSegments(verbose, "（精算）", isRefinedPass: true);

            IsRefined = true;
            LogStageRange("精算完成。汇流点精算水位", JunctionStage!);
        }

        // ──────────────────────────────────────────────────────────────────────
        // 通用段求解逻辑（初算和精算共用）
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 将支流按汇流桩号分组，依次求解各干流段。
        /// </summary>
        private void RunSegments(int verbose, string passLabel, bool isRefinedPass)
        {
            // 将支流按桩号分组（相差 ≤ MinJunctionSeparationM 视为同一汇口）
            var groups = GroupTributariesByJunction(MinJunctionSeparationM);

            if (groups.Count != _segmentFactories.Length)
                throw new InvalidOperationException(
                    $"干流段工厂数量（{_segmentFactories.Length}）与唯一汇口数量（{groups.Count}）不匹配。" +
                    $"请为每个唯一汇口提供一个干流段工厂。");

            Solver? prevSegmentSolver = _upstreamMainSolver;

            for (int g = 0; g < groups.Count; g++)
            {
                var group = groups[g];

                // 合并本汇口处所有支流出口 + 上游段出口流量
                LogCallback?.Invoke($"汇口 {g + 1}/{groups.Count}（{group[0].JunctionChainageM / 1000.0:F2} km）：合并流量{passLabel}…");
                var combined = BuildCombinedHydrograph(group.Select(t => t.Solver), prevSegmentSolver);

                // 求解本段
                bool isLastSeg = g == groups.Count - 1;
                bool useRefined = isRefinedPass && !isLastSeg
                    && g < _refinedSegFactories.Length
                    && _refinedSegFactories[g] != null
                    && g + 1 < JunctionStages.Length
                    && JunctionStages[g + 1] != null;

                LogCallback?.Invoke($"开始计算干流第 {g + 1} 段{passLabel}…");
                if (useRefined)
                {
                    double dt = _tributaries[0].Solver.TimeStep;
                    int nk = JunctionStage!.Length;
                    var stageBC = BuildJunctionStageBC(_segmentSolvers[g]!, JunctionStages[g + 1]!, dt, nk);
                    _segmentSolvers[g] = _refinedSegFactories[g]!(stageBC);
                }
                else
                {
                    _segmentSolvers[g] = _segmentFactories[g](combined);
                }

                _segmentSolvers[g].StepCallback = StepCallback;
                _segmentSolvers[g].Run(verbose);

                // 记录本汇口水位（段首节点的绝对水位）
                JunctionStages[g] = ExtractFirstNodeStage(_segmentSolvers[g]);

                prevSegmentSolver = _segmentSolvers[g];
            }
        }

        // ──────────────────────────────────────────────────────────────────────
        // 辅助：分组
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 将已排序的支流按汇流桩号分组（相差 ≤ <paramref name="tolerance"/> m 视为同一汇口）。
        /// </summary>
        private List<List<TributaryEntry>> GroupTributariesByJunction(double tolerance)
        {
            var groups = new List<List<TributaryEntry>>();
            foreach (var t in _tributaries)
            {
                if (groups.Count == 0
                    || Math.Abs(t.JunctionChainageM - groups[^1][0].JunctionChainageM) > tolerance)
                    groups.Add(new List<TributaryEntry>());
                groups[^1].Add(t);
            }
            return groups;
        }

        /// <summary>
        /// 返回第 <paramref name="tribIndex"/> 条支流（按桩号排序）所属汇口的段索引。
        /// </summary>
        private int GetSegmentIndexForTrib(int tribIndex)
        {
            var groups = GroupTributariesByJunction(MinJunctionSeparationM);
            double ch = _tributaries[tribIndex].JunctionChainageM;
            for (int g = 0; g < groups.Count; g++)
                if (Math.Abs(groups[g][0].JunctionChainageM - ch) <= MinJunctionSeparationM)
                    return g;
            return 0;
        }

        // ──────────────────────────────────────────────────────────────────────
        // 辅助：时间步一致性校验
        // ──────────────────────────────────────────────────────────────────────

        private void ValidateTimeSteps()
        {
            double dt = _tributaries[0].Solver.TimeStep;

            void Check(Solver s, string name)
            {
                if (Math.Abs(s.TimeStep - dt) > TimeStepRelTol * dt)
                    throw new InvalidOperationException(
                        $"各上游求解器时间步长不一致，无法正确合并汇流流量。\n" +
                        $"  支流1 dt = {dt} s，{name} dt = {s.TimeStep} s。\n" +
                        $"  请确保所有支流和干流段使用相同的时间步长。");
            }

            for (int i = 1; i < _tributaries.Length; i++)
                Check(_tributaries[i].Solver, $"支流 {i + 1}");
            if (_upstreamMainSolver != null)
                Check(_upstreamMainSolver, "干流上游段");
        }

        // ──────────────────────────────────────────────────────────────────────
        // 辅助：水位提取
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>从干流段首节点提取绝对水位过程线（m）。</summary>
        private static double[] ExtractFirstNodeStage(Solver s)
        {
            int nk = s.TimeLevel + 1;
            var stages = new double[nk];
            for (int k = 0; k < nk; k++)
                stages[k] = s.Level![k, 0];
            return stages;
        }

        // ──────────────────────────────────────────────────────────────────────
        // 辅助：构造汇流点水位边界
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 为指定上游求解器构造"汇流点水位"下游边界条件（<see cref="BoundaryConditionType.StageHydrograph"/>）。
        /// </summary>
        private static Boundary BuildJunctionStageBC(
            Solver upstreamSolver, double[] junctionStage, double dt, int nk)
        {
            int    lastNode    = upstreamSolver.NumberOfNodes - 1;
            double bedAtOutlet = upstreamSolver.BedProfile![lastNode];

            int nkActual = Math.Min(nk, upstreamSolver.TimeLevel + 1);
            var depthTable = new double[nkActual, 2];
            for (int k = 0; k < nkActual; k++)
            {
                double depth = junctionStage[k] - bedAtOutlet;
                if (depth < MinimumOutletDepth) depth = MinimumOutletDepth;
                depthTable[k, 0] = k * dt;
                depthTable[k, 1] = depth;
            }

            return new Boundary(
                BoundaryConditionType.StageHydrograph,
                chainage:     0,
                bedLevel:     0,
                initialDepth: depthTable[0, 1],
                hydrograph:   new Hydrograph(table: depthTable));
        }

        // ──────────────────────────────────────────────────────────────────────
        // 辅助：合并流量过程线
        // ──────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 将本汇口处所有支流出口和上游段出口流量按时步对齐求和。
        /// </summary>
        private static Hydrograph BuildCombinedHydrograph(
            IEnumerable<Solver> tribs,
            Solver? upMain)
        {
            var tribList = tribs.ToList();
            if (tribList.Count == 0 && upMain == null)
                throw new ArgumentException("至少需要一个输入求解器。");

            // 取最短时间序列（若各段步数不同则取最少步数）
            int nk = tribList.Count > 0
                ? tribList.Min(s => s.TimeLevel + 1)
                : upMain!.TimeLevel + 1;
            if (upMain != null) nk = Math.Min(nk, upMain.TimeLevel + 1);

            double dt = tribList.Count > 0 ? tribList[0].TimeStep : upMain!.TimeStep;

            var table = new double[nk, 2];
            for (int k = 0; k < nk; k++)
            {
                double q = 0;
                foreach (var s in tribList)
                    q += s.Flow![k, s.NumberOfNodes - 1];
                if (upMain != null)
                    q += upMain.Flow![k, upMain.NumberOfNodes - 1];
                table[k, 0] = k * dt;
                table[k, 1] = q;
            }
            return new Hydrograph(table: table);
        }

        // ──────────────────────────────────────────────────────────────────────
        // 辅助：日志
        // ──────────────────────────────────────────────────────────────────────

        private void LogStageRange(string prefix, double[] stage)
        {
            double min = stage[0], max = stage[0];
            for (int k = 1; k < stage.Length; k++)
            {
                if (stage[k] < min) min = stage[k];
                if (stage[k] > max) max = stage[k];
            }
            LogCallback?.Invoke($"{prefix}：{min:F3}～{max:F3} m。");
        }
    }
}
