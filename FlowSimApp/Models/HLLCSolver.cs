using System;
using System.Diagnostics;

namespace FlowSim.Models
{
    /// <summary>
    /// HLLC（Harten-Lax-van Leer-Contact）Riemann 求解器，用于一维圣维南方程组。
    /// <para>
    /// 算法概述：
    /// <list type="bullet">
    ///   <item>
    ///     HLLC 是一种 Godunov 型有限体积格式，在 HLL（两波）格式的基础上
    ///     引入接触波（Contact wave），恢复了被 HLL 格式抹平的接触间断。
    ///   </item>
    ///   <item>
    ///     守恒变量：U = [A, Q]（过水面积、流量）；
    ///     数值通量：F = [Q, Q²/A + gAD/2]，其中 D = A/T 为水力深度（水面宽 T 的近似）；
    ///     源项：S = [q_lat, gA(S₀ - Sf)]，分别为净旁侧流量和床坡-摩阻合力。
    ///   </item>
    ///   <item>
    ///     在每个界面（i+1/2）处，以左右单元状态为输入计算 HLLC 数值通量，
    ///     再对每个单元进行守恒更新：U_i^{n+1} = U_i^n - Δt/Δx·(F_{i+1/2} - F_{i-1/2}) + S_i·Δt。
    ///   </item>
    ///   <item>
    ///     稳定性要求：CFL 条件 |V ± c| ≤ Δx/Δt（与 Lax-Friedrichs 格式相同）。
    ///     每时步完成后调用 <see cref="CheckCflAll"/> 检验，违反时抛出异常。
    ///   </item>
    ///   <item>
    ///     边界节点（i=0 和 i=N-1）采用"常数外推虚节点"法生成边界通量，
    ///     更新后再根据给定主边界条件（流量过程线 or 水深）覆盖对应的 Q 或水深值。
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    public class HLLCSolver : Solver
    {
        /// <summary>
        /// CFL 警告阈值：CFL 超过此值时抛出异常，介于 1.0 和此值之间时仅发出警告。
        /// 默认值 1.05（允许 5% 以内的 CFL 超限）。
        /// </summary>
        public double CflWarningThreshold { get; set; } = 1.05;

        /// <summary>每个时步的最大 CFL 数数组 [时间层]（仿真完成后可读取）。</summary>
        public double[]? MaxCflPerStep { get; private set; }

        /// <summary>最近一次时步推进所用的子步数（自适应子步时 &gt; 1）。</summary>
        private int _lastNSub = 1;

        /// <summary>
        /// CFL 超限警告回调（可选）。
        /// 当某步 CFL 在 1 到 <see cref="CflWarningThreshold"/> 之间时触发，
        /// 参数为警告消息字符串。
        /// </summary>
        public Action<string>? CflWarningCallback { get; set; }

        /// <summary>
        /// 构造 HLLC 求解器。
        /// </summary>
        /// <param name="channel">河道对象。</param>
        /// <param name="timeStep">时间步长（秒）。</param>
        /// <param name="spatialStep">目标空间步长（m）。</param>
        /// <param name="simulationTime">总模拟时长（秒）。</param>
        /// <param name="fitSpatialStep">是否微调空间步长（默认 true）。</param>
        public HLLCSolver(Channel channel, double timeStep, double spatialStep, double simulationTime,
                          bool fitSpatialStep = true)
            : base(channel, timeStep, spatialStep, simulationTime, fitSpatialStep)
        {
            InitializeT0();
        }

        /// <summary>
        /// 执行 HLLC 显式仿真，逐时间层推进。
        /// <para>
        /// 每个时间层步骤：
        /// 1. 计算所有界面（含虚节点）处的 HLLC 数值通量；
        /// 2. 对所有节点应用守恒更新 + 源项；
        /// 3. 用主边界条件覆盖端节点；
        /// 4. 检验 CFL 条件。
        /// </para>
        /// </summary>
        public override void Run(int verbose = 1)
        {
            bool running = true;
            MaxCflPerStep = new double[NumberOfTimeLevels];
            var swTotal = Stopwatch.StartNew();
            var swStep  = new Stopwatch();

            while (running)
            {
                swStep.Restart();
                TimeLevel++;
                if (TimeLevel >= NumberOfTimeLevels)
                {
                    TimeLevel = NumberOfTimeLevels - 1;
                    running = false;
                    break;
                }

                if (verbose >= 1) Console.WriteLine($"\n> Time level #{TimeLevel}");

                AdvanceWithSubStepping();

                double maxCfl = CheckCflAll();
                MaxCflPerStep[TimeLevel] = maxCfl;

                swStep.Stop();
                StepCallback?.Invoke(TimeLevel, NumberOfTimeLevels - 1,
                                     swStep.Elapsed.TotalMilliseconds,
                                     swTotal.Elapsed.TotalSeconds);
            }

            base.Finalize(verbose);
        }

        // ---- 时间步推进 ----

        /// <summary>CFL 安全系数：子步推进时要求每子步 CFL ≤ 此值（0.9 = 90% Courant 限制）。</summary>
        private const double CflSafetyFactor = 0.9;

        /// <summary>自适应子步的最大子步数上限（2 的整次幂，方便与重试次数对应）。</summary>
        private const int MaxNSub = 4096;

        /// <summary>
        /// 推进一个用户可见的时间步，自适应地细分为若干子步以满足 CFL 稳定性条件。
        /// <para>
        /// 算法（自适应重试）：
        /// 1. 保存 TimeLevel-1 行的初始状态；
        /// 2. 根据当前时层波速估算初始 nSub；
        /// 3. 执行 nSub 个子步，将结果写入 TimeLevel 行；
        /// 4. 检查结果状态的每子步 CFL（max|V±c| · dtSub / Δx）；
        ///    若 &gt; CflSafetyFactor，则恢复初始状态并以更大的 nSub 重试，直至稳定或达到上限。
        /// </para>
        /// </summary>
        private void AdvanceWithSubStepping()
        {
            int    n      = NumberOfNodes;
            double tBase0 = (TimeLevel - 1) * TimeStep;

            // ── 1. 保存 TimeLevel-1 行（初始状态），用于自适应重试 ──
            var savedDepth = new double[n];
            var savedFlow  = new double[n];
            for (int i = 0; i < n; i++)
            {
                savedDepth[i] = Depth![TimeLevel - 1, i];
                savedFlow[i]  = Flow![TimeLevel - 1, i];
            }

            // ── 2. 根据当前状态估算初始 nSub ──
            double maxWave = ComputeMaxWaveSpeedAt(TimeLevel - 1);
            int nSub = 1;
            if (maxWave > 0)
            {
                double dtSafe = CflSafetyFactor * SpatialStep / maxWave;
                nSub = Math.Max(1, (int)Math.Ceiling(TimeStep / dtSafe));
            }

            // ── 3. 自适应重试循环 ──
            // 最多重试 log₂(MaxNSub) 次，即每次 nSub 至少翻倍时能在 log₂(MaxNSub) 步内达到上限
            int maxAttempts = (int)Math.Ceiling(Math.Log(MaxNSub, 2));
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                // 恢复 TimeLevel-1 到初始状态（每次重试前必须重置）
                for (int i = 0; i < n; i++)
                {
                    Depth![TimeLevel - 1, i] = savedDepth[i];
                    Flow![TimeLevel - 1, i]  = savedFlow[i];
                }

                // 执行 nSub 个子步
                DoSubSteps(nSub, tBase0, savedDepth, savedFlow);

                // 检查结果状态的每子步 CFL
                double maxWaveNew  = ComputeMaxWaveSpeedAt(TimeLevel);
                double dtSub       = TimeStep / nSub;
                double cflPerSub   = maxWaveNew > 0 ? maxWaveNew * dtSub / SpatialStep : 0;

                _lastNSub = nSub;

                // 浮点比较容差：避免因计算精度导致 cflPerSub 略超 CflSafetyFactor 而触发不必要的重试
                if (cflPerSub <= CflSafetyFactor * (1.0 + 1e-9) || nSub >= MaxNSub)
                    return;  // 稳定，或已达到子步上限

                // 根据结果波速重新估算所需 nSub，并重试
                // 若 maxWaveNew 为非有限数（∞ 或 NaN），直接跳到子步上限以终止循环。
                // 注意：(int)double.PositiveInfinity = int.MinValue（C# 未定义溢出行为），
                // 会导致 nSub 每次只增加 1，永远无法到达 MaxNSub，造成无限循环风险。
                if (!double.IsFinite(maxWaveNew))
                {
                    nSub = MaxNSub;
                }
                else
                {
                    int nSubNew = (int)Math.Ceiling(maxWaveNew * TimeStep / (CflSafetyFactor * SpatialStep));
                    nSub = Math.Min(Math.Max(nSubNew, nSub + 1), MaxNSub);
                }
            }
        }

        /// <summary>
        /// 执行 <paramref name="nSub"/> 个子步，将结果写入 <c>Depth/Flow[TimeLevel, :]</c>。
        /// 每个子步从 <c>TimeLevel-1</c> 行读取状态，利用 <c>TimeLevel</c> 行暂存中间状态。
        /// 子步全部完成后，<c>TimeLevel-1</c> 行恢复为 <paramref name="initDepth"/>/<paramref name="initFlow"/>。
        /// </summary>
        private void DoSubSteps(int nSub, double tBase0, double[] initDepth, double[] initFlow)
        {
            if (nSub == 1)
            {
                AdvanceTimeStep(TimeStep, tBase0);
                return;
            }

            int    n    = NumberOfNodes;
            double dtSub = TimeStep / nSub;

            for (int sub = 0; sub < nSub; sub++)
            {
                double tBase = tBase0 + sub * dtSub;
                AdvanceTimeStep(dtSub, tBase);

                // 若不是最后一子步，将本子步结果（TimeLevel 行）复制到 TimeLevel-1 行，
                // 作为下一子步的起始状态
                if (sub < nSub - 1)
                {
                    for (int i = 0; i < n; i++)
                    {
                        Depth![TimeLevel - 1, i] = Depth[TimeLevel, i];
                        Flow![TimeLevel - 1, i]  = Flow[TimeLevel, i];
                    }
                }
            }

            // 还原 TimeLevel-1 行为原始历史数据
            for (int i = 0; i < n; i++)
            {
                Depth![TimeLevel - 1, i] = initDepth[i];
                Flow![TimeLevel - 1, i]  = initFlow[i];
            }
        }

        /// <summary>
        /// 计算指定时间层 <paramref name="level"/> 所有节点的最大物理波速 max(|V ± c|)。
        /// </summary>
        private double ComputeMaxWaveSpeedAt(int level)
        {
            double maxWave = 0;
            for (int i = 0; i < NumberOfNodes; i++)
            {
                double A = AreaAt(level, i);
                if (A < 1e-10) continue;
                double Q = FlowAt(level, i);
                double T = Channel.TopWidth(i, WaterLevelAt(level, i));
                double D = T > 1e-10 ? A / T : 0;
                double c = Math.Sqrt(Hydraulics.G * Math.Max(D, 0));
                double V = Q / A;
                double w = Math.Max(Math.Abs(V + c), Math.Abs(V - c));
                if (w > maxWave) maxWave = w;
            }
            return maxWave;
        }

        /// <summary>
        /// 推进一个时间步（长度为 <paramref name="dt"/>，起始绝对时刻为 <paramref name="tBase"/>）：
        /// 计算所有界面的 HLLC 通量，应用守恒更新，处理边界条件。
        /// 读取状态来自 <c>Depth/Flow[TimeLevel-1, :]</c>，结果写入 <c>Depth/Flow[TimeLevel, :]</c>。
        /// </summary>
        /// <param name="dt">本步时间步长（秒），子步时传入 <c>TimeStep/nSub</c>。</param>
        /// <param name="tBase">本步起始绝对时刻（秒）。</param>
        private void AdvanceTimeStep(double dt, double tBase)
        {
            int    n    = NumberOfNodes;
            double dx   = SpatialStep;
            double tCur = tBase + dt;          // 本步结束时刻
            double tMid = tBase + 0.5 * dt;    // 本步中间时刻（用于旁侧流量）
            double qLat = Channel.GetNetLateralFlowPerLength(tMid);

            // 计算所有内部界面（节点 0~1, 1~2, …, N-2~N-1）处的 HLLC 通量
            // fA[k]、fQ[k] 分别为界面 k+1/2（即节点 k 与 k+1 之间）的通量
            var fA = new double[n - 1];
            var fQ = new double[n - 1];
            for (int k = 0; k < n - 1; k++)
                ComputeInterfaceFlux(k, k + 1, out fA[k], out fQ[k]);

            // 对每个节点进行守恒更新
            // 内部节点（1 到 N-2）：完整的守恒更新
            for (int i = 1; i < n - 1; i++)
                UpdateInteriorNode(i, fA[i - 1], fA[i], fQ[i - 1], fQ[i], dx, dt, qLat);

            // 端节点（0 和 N-1）：先用虚节点界面通量更新，再用主边界条件覆盖
            UpdateUpstreamNode(n, fA, fQ, dx, dt, tCur, tMid, qLat);
            UpdateDownstreamNode(n, fA, fQ, dx, dt, tCur, tMid, qLat);
        }

        /// <summary>
        /// 计算节点 L 与节点 R 之间界面处的 HLLC 数值通量。
        /// </summary>
        private void ComputeInterfaceFlux(int L, int R, out double fa, out double fq)
        {
            double A_L = AreaAt(TimeLevel - 1, L);
            double Q_L = FlowAt(TimeLevel - 1, L);
            double T_L = Channel.TopWidth(L, WaterLevelAt(TimeLevel - 1, L));

            double A_R = AreaAt(TimeLevel - 1, R);
            double Q_R = FlowAt(TimeLevel - 1, R);
            double T_R = Channel.TopWidth(R, WaterLevelAt(TimeLevel - 1, R));

            HLLCFlux(A_L, Q_L, T_L, A_R, Q_R, T_R, out fa, out fq);
        }

        /// <summary>
        /// 守恒更新内部节点 i 的 A（进而反算水深）和 Q。
        /// </summary>
        private void UpdateInteriorNode(int i,
            double fA_left, double fA_right,
            double fQ_left, double fQ_right,
            double dx, double dt, double qLat)
        {
            double A_i = AreaAt(TimeLevel - 1, i);
            double Q_i = FlowAt(TimeLevel - 1, i);

            // 床坡源项：S₀ = -(z_{i+1} - z_{i-1}) / (2Δx)（中心差分）
            double z_prev = Channel.BedLevelAt(i - 1);
            double z_next = Channel.BedLevelAt(i + 1);
            double S0 = -(z_next - z_prev) / (2.0 * dx);

            // 摩阻坡降 Sf（等效能量坡度）
            double Sf = SeAt(TimeLevel - 1, i);

            // 守恒更新（连续性 + 动量）
            double newA = A_i - dt / dx * (fA_right - fA_left) + qLat * dt;
            double newQ = Q_i - dt / dx * (fQ_right - fQ_left)
                               + Hydraulics.G * A_i * (S0 - Sf) * dt;

            // 正定性保持：面积不得为负；若面积为零（干断面），同时将流量归零，
            // 防止后续步骤出现 Q≠0、A=0 的矛盾状态，导致波速趋于无穷大或 NaN。
            // Positivity preservation: area must not be negative; if area is zero (dry section),
            // also zero out the flow to prevent the contradictory Q≠0, A=0 state that
            // would cause wave speed to blow up or produce NaN in subsequent steps.
            // 数值守卫：若通量差运算产生 NaN 或 Inf（如两侧通量均溢出），将节点重置为干断面，
            // 防止非有限值在后续时步中持续传播。
            if (newA <= 0 || !double.IsFinite(newA))
            {
                Depth![TimeLevel, i] = 0;
                Flow![TimeLevel, i]  = 0;
            }
            else
            {
                double h = AreaToDepth(i, newA);
                // 当反算水深为 0（极小干断面），同步将流量归零，防止 Q≠0/h=0 导致波速爆炸。
                // 若 newQ 也为非有限值，同样归零，避免 NaN 在结果数组中持续传播。
                Depth![TimeLevel, i] = h;
                Flow![TimeLevel, i]  = h > 0 && double.IsFinite(newQ) ? newQ : 0;
            }
        }

        /// <summary>
        /// 更新上游端节点（i=0）。
        /// 用虚节点（常数外推）计算左侧界面通量，进行完整守恒更新，
        /// 然后根据主边界条件覆盖 Q 或水深。
        /// </summary>
        private void UpdateUpstreamNode(int n,
            double[] fA, double[] fQ,
            double dx, double dt, double tCur, double tMid, double qLat)
        {
            // 虚节点（i=-1）：常数外推，状态等于节点 0
            double A_ghost = AreaAt(TimeLevel - 1, 0);
            double Q_ghost = FlowAt(TimeLevel - 1, 0);
            double T_ghost = Channel.TopWidth(0, WaterLevelAt(TimeLevel - 1, 0));

            double A_0 = AreaAt(TimeLevel - 1, 0);
            double Q_0 = FlowAt(TimeLevel - 1, 0);
            double T_0 = Channel.TopWidth(0, WaterLevelAt(TimeLevel - 1, 0));

            // 左虚节点到节点0的界面通量
            HLLCFlux(A_ghost, Q_ghost, T_ghost, A_0, Q_0, T_0, out double fA_ghost, out double fQ_ghost);

            // 床坡（单侧差分）
            double S0 = -(Channel.BedLevelAt(1) - Channel.BedLevelAt(0)) / dx;
            double Sf = SeAt(TimeLevel - 1, 0);

            double newA = A_0 - dt / dx * (fA[0] - fA_ghost) + qLat * dt;
            double newQ = Q_0 - dt / dx * (fQ[0] - fQ_ghost)
                               + Hydraulics.G * A_0 * (S0 - Sf) * dt;

            if (Channel.UpstreamBoundary.IsFlowDependent)
            {
                // 流量类边界：用连续性方程算水深，Q 从过程线获取
                double hGuess = AreaToDepth(0, newA);
                double bc_Q = Channel.UpstreamBoundary.Hydrograph?.GetAt(tCur)
                              ?? Hydraulics.NormalFlow(
                                   Channel.XsAtNode![0].BedSlope ?? 0,
                                   Channel.XsAtNode![0].Conveyance(Channel.XsAtNode![0].ZMin + hGuess));
                Depth![TimeLevel, 0] = hGuess;
                // 当水深为0（干断面）时，流量也归零以保持 h=0/Q=0 一致性
                Flow![TimeLevel, 0]  = hGuess > 0 ? bc_Q : 0;
            }
            else
            {
                // 水深类边界：用动量方程算 Q，水深取边界给定值
                double targetDepth = Channel.UpstreamBoundary.InitialDepth ?? DepthAt(0, 0);
                Depth![TimeLevel, 0] = targetDepth;
                Flow![TimeLevel, 0]  = double.IsFinite(newQ) ? newQ : 0;
            }
        }

        /// <summary>
        /// 更新下游端节点（i=N-1）。
        /// 用虚节点（常数外推）计算右侧界面通量，进行完整守恒更新，
        /// 然后根据主边界条件覆盖 Q 或水深。
        /// </summary>
        private void UpdateDownstreamNode(int n,
            double[] fA, double[] fQ,
            double dx, double dt, double tCur, double tMid, double qLat)
        {
            int last = n - 1;

            // 虚节点（i=N）：常数外推，状态等于节点 N-1
            double A_ghost = AreaAt(TimeLevel - 1, last);
            double Q_ghost = FlowAt(TimeLevel - 1, last);
            double T_ghost = Channel.TopWidth(last, WaterLevelAt(TimeLevel - 1, last));

            double A_n = AreaAt(TimeLevel - 1, last);
            double Q_n = FlowAt(TimeLevel - 1, last);
            double T_n = Channel.TopWidth(last, WaterLevelAt(TimeLevel - 1, last));

            // 节点 N-1 到右虚节点的界面通量
            HLLCFlux(A_n, Q_n, T_n, A_ghost, Q_ghost, T_ghost, out double fA_ghost, out double fQ_ghost);

            // 床坡（单侧差分）
            double S0 = -(Channel.BedLevelAt(last) - Channel.BedLevelAt(last - 1)) / dx;
            double Sf = SeAt(TimeLevel - 1, last);

            double newA = A_n - dt / dx * (fA_ghost - fA[last - 1]) + qLat * dt;
            double newQ = Q_n - dt / dx * (fQ_ghost - fQ[last - 1])
                               + Hydraulics.G * A_n * (S0 - Sf) * dt;

            if (Channel.DownstreamBoundary.IsFlowDependent)
            {
                // 流量类边界：连续性方程算水深，Q 从边界条件反算
                double hGuess = AreaToDepth(last, newA);
                double bc_Q = -Channel.DownstreamBoundary.ConditionResidual(hGuess, 0, tCur);
                Depth![TimeLevel, last] = hGuess;
                // 当水深为0（干断面）时，流量也归零以保持 h=0/Q=0 一致性
                Flow![TimeLevel, last]  = hGuess > 0 ? bc_Q : 0;
            }
            else
            {
                // 水深类边界：动量方程算 Q，水深由调蓄或固定深度给定
                double vol = 0.5 * (FlowAt(TimeLevel - 1, last) + newQ) * dt;
                double targetDepth = -(Channel.DownstreamBoundary.ConditionResidual(
                    DepthAt(TimeLevel - 1, last), newQ, tCur, dt, vol) - DepthAt(TimeLevel - 1, last));
                Depth![TimeLevel, last] = Math.Max(targetDepth, 0.001);
                Flow![TimeLevel, last]  = double.IsFinite(newQ) ? newQ : 0;
            }
        }

        // ---- HLLC 通量核心 ----

        /// <summary>
        /// 计算 HLLC Riemann 通量。
        /// <para>
        /// 守恒变量 U = [A, Q]；物理通量 F = [Q, Q²/A + gAD/2]（D = A/T 为水力深度）。
        /// 波速估算（Einfeldt 法）：
        ///   S_L = min(V_L − c_L, V_R − c_R)，S_R = max(V_L + c_L, V_R + c_R)；
        /// 接触波速（Batten 等, 1997）：
        ///   S_* = (P_L − P_R + A_L·V_L·(S_L − V_L) − A_R·V_R·(S_R − V_R))
        ///         / (A_L·(S_L − V_L) − A_R·(S_R − V_R))；
        /// 中间态：A_K* = A_K·(S_K − V_K)/(S_K − S_*)，Q_K* = A_K*·S_*。
        /// </para>
        /// </summary>
        private static void HLLCFlux(
            double A_L, double Q_L, double T_L,
            double A_R, double Q_R, double T_R,
            out double fa, out double fq)
        {
            const double eps = 1e-10;

            // 防止面积/宽度为零（干断面）
            A_L = Math.Max(A_L, eps);
            A_R = Math.Max(A_R, eps);
            T_L = Math.Max(T_L, eps);
            T_R = Math.Max(T_R, eps);

            double g   = Hydraulics.G;
            double V_L = Q_L / A_L;
            double V_R = Q_R / A_R;
            double D_L = A_L / T_L;  // 水力深度（液压深度）
            double D_R = A_R / T_R;
            double c_L = Math.Sqrt(g * D_L);  // 浅水波速
            double c_R = Math.Sqrt(g * D_R);

            // 静水压力项近似 P = g·A·D/2
            double P_L = 0.5 * g * A_L * D_L;
            double P_R = 0.5 * g * A_R * D_R;

            // 物理通量 F_K = [Q_K, Q_K·V_K + P_K]
            double fA_L = Q_L;
            double fQ_L = Q_L * V_L + P_L;
            double fA_R = Q_R;
            double fQ_R = Q_R * V_R + P_R;

            // ── Einfeldt 波速估算 ──
            double S_L = Math.Min(V_L - c_L, V_R - c_R);
            double S_R = Math.Max(V_L + c_L, V_R + c_R);

            // 若所有波均向同一侧传播，直接取上风通量
            if (S_L >= 0) { fa = fA_L; fq = fQ_L; return; }
            if (S_R <= 0) { fa = fA_R; fq = fQ_R; return; }

            // ── 接触波速 S_* （Batten et al., 1997）──
            double denom = A_L * (S_L - V_L) - A_R * (S_R - V_R);
            double S_star;
            if (Math.Abs(denom) < eps)
            {
                // 退化情形：取两侧速度算术平均
                S_star = 0.5 * (V_L + V_R);
            }
            else
            {
                S_star = (P_L - P_R + A_L * V_L * (S_L - V_L) - A_R * V_R * (S_R - V_R)) / denom;
            }
            // 将 S_* 夹在 [S_L, S_R] 内，确保数值稳定
            S_star = Math.Max(S_L, Math.Min(S_R, S_star));

            // ── 计算 HLLC 通量 ──
            if (S_star >= 0)
            {
                // 接触波向右 → 取左 HLLC 中间态
                double factor = (S_L - V_L) / (S_L - S_star);   // > 0（因 S_L < V_L 且 S_L ≤ S_*）
                double A_Ls = A_L * factor;
                double Q_Ls = A_Ls * S_star;
                fa = fA_L + S_L * (A_Ls - A_L);
                fq = fQ_L + S_L * (Q_Ls - Q_L);
            }
            else
            {
                // 接触波向左 → 取右 HLLC 中间态
                double factor = (S_R - V_R) / (S_R - S_star);   // > 0（因 S_R > V_R 且 S_R ≥ S_*）
                double A_Rs = A_R * factor;
                double Q_Rs = A_Rs * S_star;
                fa = fA_R + S_R * (A_Rs - A_R);
                fq = fQ_R + S_R * (Q_Rs - Q_R);
            }
        }

        // ---- CFL 检验 ----

        /// <summary>
        /// 检验所有节点的 CFL 稳定性条件：|V ± c| · dtSub / Δx ≤ 阈值。
        /// <para>
        /// 使用等效子步时间步长 dtSub = TimeStep / _lastNSub 计算 CFL，
        /// 从而正确反映自适应子步推进的实际稳定性，而非用户设置的原始步长。
        /// </para>
        /// </summary>
        /// <returns>本时步所有节点中最大的每子步 CFL 数。</returns>
        private double CheckCflAll()
        {
            double stepMaxCfl = 0;
            int    maxNode    = 0;

            // 等效子步时间步长：自适应子步后反映实际稳定性
            double effectiveDt = TimeStep / _lastNSub;

            for (int i = 0; i < NumberOfNodes; i++)
            {
                double A = AreaAt(TimeLevel, i);
                double Q = FlowAt(TimeLevel, i);
                if (A < 1e-10) continue;

                double V = Q / A;
                double T = Channel.TopWidth(i, WaterLevelAt(TimeLevel, i));
                double D = T > 1e-10 ? A / T : 0;
                double c = Math.Sqrt(Hydraulics.G * Math.Max(D, 0));

                double maxCelerity = Math.Max(Math.Abs(V + c), Math.Abs(V - c));
                double cfl = maxCelerity * effectiveDt / SpatialStep;
                if (cfl > stepMaxCfl) { stepMaxCfl = cfl; maxNode = i; }
            }

            if (stepMaxCfl > CflWarningThreshold)
                throw new InvalidOperationException(
                    $"CFL condition failed at i={maxNode}, k={TimeLevel}. CFL={stepMaxCfl:F3}");

            if (stepMaxCfl > 1.0)
            {
                string msg = $"[CFL 警告] 步骤 k={TimeLevel}, 节点 i={maxNode}: CFL={stepMaxCfl:F3} > 1（低于阈值 {CflWarningThreshold:F2}，继续计算）";
                if (CflWarningCallback != null)
                    CflWarningCallback(msg);
                else
                    Console.WriteLine(msg);
            }

            return stepMaxCfl;
        }
    }
}
