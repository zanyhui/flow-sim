using System;

namespace FlowSim.Models
{
    /// <summary>
    /// Lax 格式次要边界条件类型枚举（用于处理上下游端节点的"虚节点"值）。
    /// <list type="bullet">
    ///   <item><term>Constant</term><description>恒定：虚节点与端节点取相同值（零阶外推）。</description></item>
    ///   <item><term>Mirror</term><description>镜像：虚节点取相邻内节点的值（反射条件）。</description></item>
    ///   <item><term>Linear</term><description>线性外推：虚节点 = 2×端节点 - 相邻内节点（一阶外推）。</description></item>
    /// </list>
    /// </summary>
    public enum LaxSecondaryBC { Constant, Mirror, Linear }

    /// <summary>
    /// Lax-Friedrichs 显式有限差分格式求解一维圣维南方程组。
    /// <para>
    /// 算法概述：
    /// <list type="bullet">
    ///   <item>
    ///     Lax-Friedrichs 格式是一种一阶精度显式格式：
    ///     新时层中间节点值由旧时层左右相邻两节点（i-1、i+1）的空间平均
    ///     加上有限差分修正项更新，等价于加入了数值耗散的中心格式。
    ///   </item>
    ///   <item>
    ///     连续性方程离散：A_i^新 = 0.5*(A_{i-1}+A_{i+1}) - Δt/(2Δx)*(Q_{i+1}-Q_{i-1})；
    ///     动量方程离散：Q_i^新 = 0.5*(Q_{i-1}+Q_{i+1}) - Δt*(动量通量梯度 + 重力项)。
    ///   </item>
    ///   <item>
    ///     稳定性要求：CFL 条件 |V ± c| ≤ Δx/Δt（即物理波速不得超过数值波速 NumCelerity）。
    ///     每个时步完成后调用 <see cref="CheckCflAll"/> 检验，违反时抛出异常。
    ///   </item>
    ///   <item>
    ///     端节点（i=0 和 i=N-1）需要虚节点（ghost node）：
    ///     根据 <see cref="LaxSecondaryBC"/> 类型（恒定/镜像/线性外推）生成虚节点值，
    ///     然后结合主边界条件（流量过程线 or 水深）更新端节点。
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    public class LaxSolver : Solver
    {
        /// <summary>上游端节点次要边界条件类型（决定虚节点值的计算方式）。</summary>
        public LaxSecondaryBC UpstreamSecondaryBC { get; }

        /// <summary>下游端节点次要边界条件类型。</summary>
        public LaxSecondaryBC DownstreamSecondaryBC { get; }

        /// <summary>
        /// 构造 Lax-Friedrichs 求解器。
        /// </summary>
        /// <param name="channel">河道对象。</param>
        /// <param name="timeStep">时间步长（秒）。</param>
        /// <param name="spatialStep">目标空间步长（m）。</param>
        /// <param name="simulationTime">总模拟时长（秒）。</param>
        /// <param name="upstreamSecondaryBC">上游次要边界条件类型（默认 Constant）。</param>
        /// <param name="downstreamSecondaryBC">下游次要边界条件类型（默认 Constant）。</param>
        /// <param name="fitSpatialStep">是否微调空间步长（默认 true）。</param>
        public LaxSolver(Channel channel, double timeStep, double spatialStep, double simulationTime,
                         LaxSecondaryBC upstreamSecondaryBC = LaxSecondaryBC.Constant,
                         LaxSecondaryBC downstreamSecondaryBC = LaxSecondaryBC.Constant,
                         bool fitSpatialStep = true)
            : base(channel, timeStep, spatialStep, simulationTime, fitSpatialStep)
        {
            UpstreamSecondaryBC   = upstreamSecondaryBC;
            DownstreamSecondaryBC = downstreamSecondaryBC;
            InitializeT0();   // 将初始条件写入 Depth[0,:] 和 Flow[0,:]
        }

        /// <summary>
        /// 执行 Lax-Friedrichs 显式仿真，逐时间层推进。
        /// <para>
        /// 每个时间层步骤：
        /// 1. 对所有节点（0 到 N-1）依次调用 <see cref="ComputeNode"/>；
        /// 2. 检验 CFL 条件（<see cref="CheckCflAll"/>）。
        /// 显式格式无需迭代，每时步计算量为 O(N)。
        /// </para>
        /// </summary>
        public override void Run(int verbose = 1)
        {
            bool running = true;
            while (running)
            {
                TimeLevel++;
                if (TimeLevel >= NumberOfTimeLevels)
                {
                    TimeLevel = NumberOfTimeLevels - 1;
                    running = false;
                    break;
                }

                if (verbose >= 1) Console.WriteLine($"\n> Time level #{TimeLevel}");

                // 对每个节点计算新时层值
                for (int i = 0; i < NumberOfNodes; i++)
                    ComputeNode(i);

                // 检验 CFL 稳定性条件
                CheckCflAll();
            }

            base.Finalize(verbose);
        }

        /// <summary>
        /// 生成上游虚节点（ghost node）的水力参数，
        /// 以便将端节点 i=0 的 Lax-Friedrichs 格式与内部节点统一处理。
        /// </summary>
        /// <returns>(A, Q, Y, Se)：虚节点的过水面积、流量、水位和等效能量坡度。</returns>
        private (double A, double Q, double Y, double Se) UsGhostNode()
        {
            switch (UpstreamSecondaryBC)
            {
                case LaxSecondaryBC.Mirror:
                    // 镜像：虚节点 = 相邻内节点 i=1 的值
                    return (AreaAt(TimeLevel - 1, 1), FlowAt(TimeLevel - 1, 1),
                            WaterLevelAt(TimeLevel - 1, 1), SeAt(TimeLevel - 1, 1));
                case LaxSecondaryBC.Linear:
                    // 线性外推：虚节点 = 2×端节点(0) - 相邻内节点(1)
                    return (2 * AreaAt(TimeLevel - 1, 0) - AreaAt(TimeLevel - 1, 1),
                            2 * FlowAt(TimeLevel - 1, 0) - FlowAt(TimeLevel - 1, 1),
                            2 * WaterLevelAt(TimeLevel - 1, 0) - WaterLevelAt(TimeLevel - 1, 1),
                            2 * SeAt(TimeLevel - 1, 0) - SeAt(TimeLevel - 1, 1));
                default: // Constant（零阶外推）
                    return (AreaAt(TimeLevel - 1, 0), FlowAt(TimeLevel - 1, 0),
                            WaterLevelAt(TimeLevel - 1, 0), SeAt(TimeLevel - 1, 0));
            }
        }

        /// <summary>
        /// 生成下游虚节点（ghost node）的水力参数。
        /// </summary>
        /// <returns>(A, Q, Y, Se)：虚节点的水力参数。</returns>
        private (double A, double Q, double Y, double Se) DsGhostNode()
        {
            int last = NumberOfNodes - 1;
            switch (DownstreamSecondaryBC)
            {
                case LaxSecondaryBC.Mirror:
                    // 镜像：虚节点 = 相邻内节点 last-1 的值
                    return (AreaAt(TimeLevel - 1, last - 1), FlowAt(TimeLevel - 1, last - 1),
                            WaterLevelAt(TimeLevel - 1, last - 1), SeAt(TimeLevel - 1, last - 1));
                case LaxSecondaryBC.Linear:
                    // 线性外推
                    return (2 * AreaAt(TimeLevel - 1, last) - AreaAt(TimeLevel - 1, last - 1),
                            2 * FlowAt(TimeLevel - 1, last) - FlowAt(TimeLevel - 1, last - 1),
                            2 * WaterLevelAt(TimeLevel - 1, last) - WaterLevelAt(TimeLevel - 1, last - 1),
                            2 * SeAt(TimeLevel - 1, last) - SeAt(TimeLevel - 1, last - 1));
                default: // Constant
                    return (AreaAt(TimeLevel - 1, last), FlowAt(TimeLevel - 1, last),
                            WaterLevelAt(TimeLevel - 1, last), SeAt(TimeLevel - 1, last));
            }
        }

        /// <summary>
        /// 计算节点 i 处的新时层值，分三种情况处理：
        /// - i=0：上游端节点（<see cref="ComputeUpstreamNode"/>）；
        /// - i=N-1：下游端节点（<see cref="ComputeDownstreamNode"/>）；
        /// - 其他：内部节点（Lax-Friedrichs 标准格式）。
        /// </summary>
        private void ComputeNode(int i)
        {
            if (i == 0)
                ComputeUpstreamNode();
            else if (i == NumberOfNodes - 1)
                ComputeDownstreamNode();
            else
            {
                // 内部节点：取旧时层左右相邻节点的水力参数
                double A_im1 = AreaAt(TimeLevel - 1, i - 1);
                double A_ip1 = AreaAt(TimeLevel - 1, i + 1);
                double Q_im1 = FlowAt(TimeLevel - 1, i - 1);
                double Q_ip1 = FlowAt(TimeLevel - 1, i + 1);
                double Y_im1 = WaterLevelAt(TimeLevel - 1, i - 1);
                double Y_ip1 = WaterLevelAt(TimeLevel - 1, i + 1);
                double Se_im1 = SeAt(TimeLevel - 1, i - 1);
                double Se_ip1 = SeAt(TimeLevel - 1, i + 1);

                // 用 Lax-Friedrichs 格式更新面积和流量
                double newA = NewArea(A_im1, A_ip1, Q_im1, Q_ip1);
                double newQ = NewFlow(A_im1, A_ip1, Q_im1, Q_ip1, Y_im1, Y_ip1, Se_im1, Se_ip1);

                // 将新过水面积反算为水深（通过 Brent 方法反查面积-水深关系）
                Depth![TimeLevel, i] = AreaToDepth(i, newA);
                Flow![TimeLevel, i]  = newQ;
            }
        }

        /// <summary>
        /// 计算上游端节点（i=0）的新时层值。
        /// <para>
        /// 策略：
        /// - 若上游边界为流量类型（FlowHydrograph/NormalDepth/RatingCurve）：
        ///   用 Lax 格式计算新过水面积（进而得到水深），Q 从边界条件直接读取；
        /// - 若上游边界为水深类型（FixedDepth/StageHydrograph）：
        ///   用 Lax 格式计算新流量，水深从边界条件直接给定。
        /// </para>
        /// </summary>
        private void ComputeUpstreamNode()
        {
            var (ghostA, ghostQ, ghostY, ghostSe) = UsGhostNode();

            if (Channel.UpstreamBoundary.IsFlowDependent)
            {
                // 流量类边界：Lax 更新 A（= 连续性方程），Q 从过程线或正常流公式获取
                double newA  = NewArea(ghostA, AreaAt(TimeLevel - 1, 1), ghostQ, FlowAt(TimeLevel - 1, 1));
                double t     = TimeLevel * TimeStep;
                double hGuess = AreaToDepth(0, newA);   // 由面积反算水深

                // 从过程线直接读取目标流量（若为正常流则用均匀流公式）
                double newQ = Channel.UpstreamBoundary.Hydrograph?.GetAt(t) ?? Hydraulics.NormalFlow(
                    Channel.XsAtNode![0].BedSlope ?? 0,
                    Channel.XsAtNode![0].Conveyance(Channel.XsAtNode![0].ZMin + hGuess));
                Depth![TimeLevel, 0] = hGuess;
                Flow![TimeLevel, 0]  = newQ;
            }
            else
            {
                // 水深类边界：Lax 更新 Q，水深取边界给定的目标值
                double newQ = NewFlow(ghostA, AreaAt(TimeLevel - 1, 1), ghostQ, FlowAt(TimeLevel - 1, 1),
                                      ghostY, WaterLevelAt(TimeLevel - 1, 1), ghostSe, SeAt(TimeLevel - 1, 1));
                double t           = TimeLevel * TimeStep;
                double targetDepth = Channel.UpstreamBoundary.InitialDepth ?? DepthAt(0, 0);
                Depth![TimeLevel, 0] = targetDepth;
                Flow![TimeLevel, 0]  = newQ;
            }
        }

        /// <summary>
        /// 计算下游端节点（i=N-1）的新时层值。
        /// <para>
        /// 策略与上游类似：
        /// - 流量类边界：Lax 更新 A，Q 由边界条件残差反算；
        /// - 水深类边界：Lax 更新 Q，水深由本时步集总调蓄或固定深度给定。
        /// </para>
        /// </summary>
        private void ComputeDownstreamNode()
        {
            int last = NumberOfNodes - 1;
            var (ghostA, ghostQ, ghostY, ghostSe) = DsGhostNode();

            if (Channel.DownstreamBoundary.IsFlowDependent)
            {
                // 流量类边界：Lax 更新 A，Q = -ConditionResidual(...) 中的目标值
                double newA   = NewArea(AreaAt(TimeLevel - 1, last - 1), ghostA, FlowAt(TimeLevel - 1, last - 1), ghostQ);
                double t      = TimeLevel * TimeStep;
                double hGuess = AreaToDepth(last, newA);
                // ConditionResidual = Q - target，故 Q = target = -(-Q + target) + Q？
                // 简化：直接从残差方程反算 Q（残差=0 时 Q=目标）
                double newQ = -Channel.DownstreamBoundary.ConditionResidual(hGuess, 0, t);
                Depth![TimeLevel, last] = hGuess;
                Flow![TimeLevel, last]  = newQ;
            }
            else
            {
                // 水深类边界：Lax 更新 Q，水深由质量守恒或固定深度给定
                double newQ = NewFlow(AreaAt(TimeLevel - 1, last - 1), ghostA,
                                      FlowAt(TimeLevel - 1, last - 1), ghostQ,
                                      WaterLevelAt(TimeLevel - 1, last - 1), ghostY,
                                      SeAt(TimeLevel - 1, last - 1), ghostSe);
                double t   = TimeLevel * TimeStep;
                // 用梯形近似计算本时步流入体积（用于集总调蓄质量守恒）
                double vol = 0.5 * (FlowAt(TimeLevel - 1, last) + newQ) * TimeStep;
                // 从边界残差方程反算目标水深（使残差 = depth_new - target = 0）
                double targetDepth = -(Channel.DownstreamBoundary.ConditionResidual(
                    DepthAt(TimeLevel - 1, last), newQ, t, TimeStep, vol) - DepthAt(TimeLevel - 1, last));
                Depth![TimeLevel, last] = Math.Max(targetDepth, 0.001);  // 防止水深为负
                Flow![TimeLevel, last]  = newQ;
            }
        }

        /// <summary>
        /// Lax-Friedrichs 连续性格式：更新过水面积。
        /// 公式：A_i^新 = 0.5*(A_{i-1}+A_{i+1}) - Δt/(2Δx)*(Q_{i+1}-Q_{i-1})
        /// 第一项为空间平均（引入数值耗散），第二项为流量散度修正。
        /// </summary>
        /// <param name="A_im1">旧时层节点 i-1 处的过水面积（m²）。</param>
        /// <param name="A_ip1">旧时层节点 i+1 处的过水面积（m²）。</param>
        /// <param name="Q_im1">旧时层节点 i-1 处的流量（m³/s）。</param>
        /// <param name="Q_ip1">旧时层节点 i+1 处的流量（m³/s）。</param>
        /// <returns>新时层节点 i 处的过水面积（m²）。</returns>
        private double NewArea(double A_im1, double A_ip1, double Q_im1, double Q_ip1)
        {
            double avgA = LaxCellAvg(A_ip1, A_im1);      // 0.5*(A_{i+1}+A_{i-1})
            double dQ_dx = LaxSpatialDiff(Q_ip1, Q_im1); // (Q_{i+1}-Q_{i-1})/(2Δx)
            return avgA - dQ_dx * TimeStep;               // A_new = avgA - (dQ/dx)·Δt
        }

        /// <summary>
        /// Lax-Friedrichs 动量格式：更新流量。
        /// 公式：Q_i^新 = 0.5*(Q_{i-1}+Q_{i+1}) - Δt·[d(Q²/A)/dx + g·avgA·(dY/dx + avgSe)]
        /// </summary>
        /// <param name="A_im1">旧时层节点 i-1 的面积（m²）。</param>
        /// <param name="A_ip1">旧时层节点 i+1 的面积（m²）。</param>
        /// <param name="Q_im1">旧时层节点 i-1 的流量（m³/s）。</param>
        /// <param name="Q_ip1">旧时层节点 i+1 的流量（m³/s）。</param>
        /// <param name="Y_im1">旧时层节点 i-1 的绝对水位（m）。</param>
        /// <param name="Y_ip1">旧时层节点 i+1 的绝对水位（m）。</param>
        /// <param name="Se_im1">旧时层节点 i-1 的等效能量坡度（无量纲）。</param>
        /// <param name="Se_ip1">旧时层节点 i+1 的等效能量坡度（无量纲）。</param>
        /// <returns>新时层节点 i 处的流量（m³/s）。</returns>
        private double NewFlow(double A_im1, double A_ip1, double Q_im1, double Q_ip1,
                               double Y_im1, double Y_ip1, double Se_im1, double Se_ip1)
        {
            double avgQ  = LaxCellAvg(Q_ip1, Q_im1);    // 0.5*(Q_{i+1}+Q_{i-1})（数值耗散项）
            double avgA  = LaxCellAvg(A_ip1, A_im1);    // 0.5*(A_{i+1}+A_{i-1})
            double avgSe = LaxCellAvg(Se_ip1, Se_im1);  // 0.5*(Se_{i+1}+Se_{i-1})

            // 动量通量 Q²/A 的空间导数
            double dQ2A_dx = LaxSpatialDiff(Q_ip1 * Q_ip1 / Math.Max(A_ip1, 1e-6),
                                             Q_im1 * Q_im1 / Math.Max(A_im1, 1e-6));
            // 水面坡度（绝对水位的空间导数）
            double dY_dx = LaxSpatialDiff(Y_ip1, Y_im1);

            // 动量方程：Q_new = avgQ - (dQ²A_dx + g·avgA·(dY_dx + avgSe))·Δt
            return avgQ - (dQ2A_dx + Hydraulics.G * avgA * (dY_dx + avgSe)) * TimeStep;
        }

        /// <summary>
        /// Lax-Friedrichs 空间差分算子：(f_{i+1} - f_{i-1}) / (2Δx)。
        /// 使用两个相邻节点的值（不含当前节点），对应中心差分格式。
        /// </summary>
        /// <param name="ip1">节点 i+1 处的值。</param>
        /// <param name="im1">节点 i-1 处的值。</param>
        /// <returns>空间导数近似值。</returns>
        private double LaxSpatialDiff(double ip1, double im1) => 0.5 * (ip1 - im1) / SpatialStep;

        /// <summary>
        /// Lax-Friedrichs 单元均值算子：(f_{i+1} + f_{i-1}) / 2。
        /// 这是 Lax-Friedrichs 格式引入数值耗散的关键（与标准中心格式不同）。
        /// </summary>
        private double LaxCellAvg(double ip1, double im1) => 0.5 * (ip1 + im1);

        /// <summary>
        /// 将过水面积 A 反算为水深 h，利用 Brent 方法求解方程 A(h+ZMin) = A_target。
        /// 当 A≤0 时直接返回 0；若 Brent 方法失败，则用矩形断面近似（h ≈ A/B）作为备用。
        /// </summary>
        /// <param name="nodeIdx">节点索引。</param>
        /// <param name="A">目标过水面积（m²）。</param>
        /// <returns>对应水深 h（m，相对于床底）。</returns>
        private double AreaToDepth(int nodeIdx, double A)
        {
            if (A <= 0) return 0;
            var xs = Channel.XsAtNode![nodeIdx];
            double zMin = xs.ZMin;
            double hMax = 50.0;   // 搜索上限 50 m
            try
            {
                // 求解 xs.Area(h + ZMin) = A，即水深 h 使得面积等于目标值
                return Hydraulics.Brentq(h => xs.Area(h + zMin) - A, 1e-6, hMax);
            }
            catch
            {
                // 备用：矩形近似 h ≈ A / B（B = 断面宽度）
                return A / Math.Max(xs.Width, 1.0);
            }
        }

        /// <summary>
        /// 检验所有节点的 CFL 稳定性条件：|V ± c| ≤ Δx/Δt。
        /// 若任意节点的物理波速超过数值波速 NumCelerity，则仿真不稳定，抛出异常。
        /// <para>
        /// CFL 数 = max(|V+c|, |V-c|) / (Δx/Δt)，应满足 CFL ≤ 1。
        /// 其中 c = sqrt(g·D) 为浅水波速，V 为断面平均流速。
        /// </para>
        /// </summary>
        private void CheckCflAll()
        {
            for (int i = 0; i < NumberOfNodes; i++)
            {
                double A = AreaAt(TimeLevel, i);
                double Q = FlowAt(TimeLevel, i);
                if (A < 1e-10) continue;   // 干节点跳过

                double V = Q / A;                                             // 断面平均流速
                double T = Channel.TopWidth(i, WaterLevelAt(TimeLevel, i));
                double D = T > 1e-10 ? A / T : 0;                            // 水力深度
                double c = Math.Sqrt(Hydraulics.G * Math.Max(D, 0));         // 浅水波速

                // 顺逆流方向两个波速的最大绝对值
                double maxCelerity = Math.Max(Math.Abs(V + c), Math.Abs(V - c));
                if (maxCelerity > NumCelerity)
                    throw new InvalidOperationException(
                        $"CFL condition failed at i={i}, k={TimeLevel}. CFL={maxCelerity / NumCelerity:F3}");
            }
        }
    }
}
