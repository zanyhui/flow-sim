using System;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace FlowSim.Models
{
    /// <summary>
    /// Preissmann 隐式有限差分格式求解一维圣维南方程组。
    /// <para>
    /// 算法概述：
    /// <list type="bullet">
    ///   <item>
    ///     Preissmann 格式将时间导数和空间导数分别用时间和空间的加权差分近似：
    ///     时间导数：∂f/∂t ≈ (f_新平均 - f_旧平均) / Δt；
    ///     空间导数：∂f/∂x ≈ θ·(f_i+1^新 - f_i^新)/Δx + (1-θ)·(f_i+1^旧 - f_i^旧)/Δx；
    ///     θ=1 为全隐式（无条件稳定），θ=0.5 为 Crank-Nicolson（二阶精度）。
    ///   </item>
    ///   <item>
    ///     每个时间步形成以 [h_0, Q_0, h_1, Q_1, ..., h_{N-1}, Q_{N-1}] 为未知量的
    ///     非线性方程组（2N 个方程）：
    ///     1 个上游边界方程 + (N-1)×2 个内部连续性/动量方程 + 1 个下游边界方程。
    ///   </item>
    ///   <item>
    ///     用牛顿-拉弗森法迭代求解：
    ///     计算残差向量 R 和雅可比矩阵 J，求解 J·Δx = -R，更新未知量直到收敛。
    ///   </item>
    /// </list>
    /// </para>
    /// </summary>
    public class PreissmannSolver : Solver
    {
        /// <summary>Preissmann 隐式权重系数 θ（0.5～1.0；θ≥0.5 时无条件稳定）。</summary>
        public double Theta { get; }

        // 当前时间层的未知量向量 [h0, Q0, h1, Q1, ..., h_{N-1}, Q_{N-1}]
        private readonly double[] _unknowns;

        // 残差向量 R（与未知量等长，牛顿迭代中须驱动到 0）
        private readonly double[] _R;

        /// <summary>
        /// 构造 Preissmann 求解器并初始化未知量向量。
        /// </summary>
        /// <param name="channel">河道对象。</param>
        /// <param name="theta">隐式权重 θ（0.5～1.0）。</param>
        /// <param name="timeStep">时间步长（秒）。</param>
        /// <param name="spatialStep">目标空间步长（m）。</param>
        /// <param name="simulationTime">总模拟时长（秒）。</param>
        /// <param name="fitSpatialStep">是否微调空间步长（默认 true）。</param>
        public PreissmannSolver(Channel channel, double theta,
                                double timeStep, double spatialStep, double simulationTime,
                                bool fitSpatialStep = true)
            : base(channel, timeStep, spatialStep, simulationTime, fitSpatialStep)
        {
            Theta = theta;
            _unknowns = new double[NumberOfNodes * 2];   // 每个节点 2 个未知量 (h, Q)
            _R = new double[NumberOfNodes * 2];

            InitializeT0();   // 将 t=0 初始条件写入 Depth[0,:] 和 Flow[0,:]

            // 将初始条件展平为未知量向量：[h0, Q0, h1, Q1, ...]
            for (int i = 0; i < NumberOfNodes; i++)
            {
                _unknowns[2 * i]     = Channel.InitialConditions![i, 0];  // h_i
                _unknowns[2 * i + 1] = Channel.InitialConditions![i, 1];  // Q_i
            }
        }

        /// <summary>
        /// 执行 Preissmann 隐式仿真，逐时间层推进。
        /// <para>
        /// 每个时间层步骤：
        /// 1. 使用上一层未知量作为本层初始猜测值；
        /// 2. 在牛顿迭代循环中：
        ///    a. 将当前估计值写入 Depth/Flow 数组；
        ///    b. 计算残差向量 R（<see cref="ComputeResidualVector"/>）；
        ///    c. 计算雅可比矩阵 J（<see cref="ComputeJacobian"/>）；
        ///    d. 用 MathNet 求解线性方程组 J·Δ = -R；
        ///    e. 更新未知量 x += Δ；
        ///    f. 检验 ||R|| &lt; tol（欧氏范数收敛准则）。
        /// 3. 收敛后进入下一时间层。
        /// </para>
        /// </summary>
        /// <param name="verbose">输出详细程度（0=静默，1=每层，2=迭代次数，3=每步残差）。</param>
        public override void Run(int verbose = 1)
        {
            bool running = true;
            int totalIterations = 0;
            const double tolerance = 1e-4;   // 牛顿迭代收敛容差（残差欧氏范数）
            const int maxIter = 100;         // 最大迭代次数

            while (running)
            {
                TimeLevel++;   // 推进时间层计数器
                if (TimeLevel >= NumberOfTimeLevels)
                {
                    TimeLevel = NumberOfTimeLevels - 1;
                    running = false;
                    break;
                }

                if (verbose >= 1) Console.WriteLine($"\n> Time level #{TimeLevel}");

                int iteration = 0;
                bool converged = false;

                // 牛顿-拉弗森迭代循环
                while (!converged)
                {
                    iteration++;
                    if (iteration - 1 >= maxIter)
                        throw new InvalidOperationException($"Failed to converge after {maxIter} iterations.");

                    // 将当前估计值写入结果数组（作为本次线性化基点）
                    for (int i = 0; i < NumberOfNodes; i++)
                    {
                        Depth![TimeLevel, i] = _unknowns[2 * i];
                        Flow![TimeLevel, i]  = _unknowns[2 * i + 1];
                    }

                    // 计算非线性残差向量 R
                    ComputeResidualVector();

                    // 计算雅可比矩阵 J（2N × 2N 带状稀疏矩阵）
                    double[,] J = ComputeJacobian();

                    // 构造 MathNet 矩阵/向量并求解线性系统 J·Δ = -R
                    var Jm = Matrix<double>.Build.DenseOfArray(J);
                    var Rv = Vector<double>.Build.DenseOfArray(_R);
                    Vector<double> delta;
                    try { delta = Jm.Solve(-Rv); }
                    catch { throw new InvalidOperationException("Jacobian solve failed."); }

                    // 更新未知量：x_new = x_old + Δ
                    for (int k = 0; k < _unknowns.Length; k++)
                        _unknowns[k] += delta[k];

                    // 计算残差向量的欧氏范数作为收敛指标
                    double error = 0;
                    for (int k = 0; k < _R.Length; k++) error += _R[k] * _R[k];
                    error = Math.Sqrt(error);

                    if (verbose == 3) Console.WriteLine($">> Iteration #{iteration}: Error = {error}");
                    if (error < tolerance) converged = true;
                }

                if (verbose == 2) Console.WriteLine($">> {iteration} iterations.");
                totalIterations += iteration;
            }

            base.Finalize(verbose);
        }

        /// <summary>
        /// 计算当前时间层的完整残差向量 R（2N 个方程）：
        /// - R[0]               = 上游边界条件残差；
        /// - R[1+2i], R[2+2i]  = 第 i 个内部单元的连续性/动量残差（i=0..N-2）；
        /// - R[2N-1]            = 下游边界条件残差。
        /// </summary>
        private void ComputeResidualVector()
        {
            _R[0] = UpstreamResidual();                           // 上游边界
            _R[2 * NumberOfNodes - 1] = DownstreamResidual();    // 下游边界
            for (int i = 0; i < NumberOfNodes - 1; i++)
            {
                _R[1 + 2 * i] = ContinuityResidual(i);           // 连续性方程
                _R[2 + 2 * i] = MomentumResidual(i);             // 动量方程
            }
        }

        /// <summary>
        /// 计算雅可比矩阵 J（2N × 2N），J[row, col] = ∂R[row]/∂x[col]。
        /// <para>
        /// 矩阵结构（带状）：
        /// - 第 0 行：上游边界，非零列为 [0,1]（h_0, Q_0）；
        /// - 第 1+2i 行（连续性）：非零列为 [2i, 2i+1, 2(i+1), 2(i+1)+1]；
        /// - 第 2+2i 行（动量）：同上；
        /// - 最后行：下游边界，非零列为 [2N-2, 2N-1]（h_{N-1}, Q_{N-1}）。
        /// </para>
        /// </summary>
        private double[,] ComputeJacobian()
        {
            int size = 2 * NumberOfNodes;
            var J = new double[size, size];

            // 上游边界行（行 0）：仅依赖 h_0 和 Q_0
            J[0, 0] = DU_Dh();
            J[0, 1] = DU_DQ();

            // 内部节点行（连续性 + 动量各一行）
            for (int i = 0; i < NumberOfNodes - 1; i++)
            {
                int row = 1 + 2 * i;
                // 连续性方程对 [h_i, Q_i, h_{i+1}, Q_{i+1}] 的偏导
                J[row, 2 * i]             = DC_Dh_i(i);
                J[row, 2 * i + 1]         = DC_DQ_i(i);
                J[row, 2 * (i + 1)]       = DC_Dh_ip1(i);
                J[row, 2 * (i + 1) + 1]   = DC_DQ_ip1(i);
                // 动量方程对 [h_i, Q_i, h_{i+1}, Q_{i+1}] 的偏导
                J[row + 1, 2 * i]         = DM_Dh_i(i);
                J[row + 1, 2 * i + 1]     = DM_DQ_i(i);
                J[row + 1, 2 * (i + 1)]   = DM_Dh_ip1(i);
                J[row + 1, 2 * (i + 1) + 1] = DM_DQ_ip1(i);
            }

            // 下游边界行（最后行）：仅依赖 h_{N-1} 和 Q_{N-1}
            J[size - 1, size - 2] = DD_Dh();
            J[size - 1, size - 1] = DD_DQ();

            return J;
        }

        // ---- 边界残差计算 ----

        /// <summary>
        /// 上游边界条件残差 R_0 = f(h_0, Q_0, t)。
        /// 委托给 <see cref="Boundary.ConditionResidual"/>。
        /// </summary>
        private double UpstreamResidual()
        {
            double t = TimeLevel * TimeStep;   // 当前时刻（秒）
            return Channel.UpstreamBoundary.ConditionResidual(DepthAt(TimeLevel, 0), FlowAt(TimeLevel, 0), t);
        }

        /// <summary>
        /// 下游边界条件残差 R_{2N-1} = f(h_{N-1}, Q_{N-1}, t, dt, vol_in)。
        /// vol_in = 本时步下游平均流量 × Δt，用于集总调蓄库质量守恒。
        /// </summary>
        private double DownstreamResidual()
        {
            double t = TimeLevel * TimeStep;
            // 梯形近似：vol_in = 0.5*(Q_旧 + Q_新) * Δt
            double vol = 0.5 * (FlowAt(TimeLevel - 1, -1) + FlowAt(TimeLevel, -1)) * TimeStep;
            return Channel.DownstreamBoundary.ConditionResidual(DepthAt(TimeLevel, -1), FlowAt(TimeLevel, -1), t, TimeStep, vol);
        }

        /// <summary>
        /// 单元 i（节点 i～i+1 之间）的连续性方程残差。
        /// <para>
        /// 圣维南连续性方程：∂A/∂t + ∂Q/∂x = 0。
        /// Preissmann 离散：(A_新平均 - A_旧平均)/Δt + θ·(Q_{i+1}^新-Q_i^新)/Δx
        ///                  + (1-θ)·(Q_{i+1}^旧-Q_i^旧)/Δx = 0。
        /// </para>
        /// </summary>
        private double ContinuityResidual(int i)
        {
            // 时间导数：∂A/∂t ≈ (新时层平均A - 旧时层平均A) / Δt
            double dA_dt = TimeDiff(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));

            // 空间导数：∂Q/∂x（Preissmann 加权差分）
            double dQ_dx = SpatialDiff(
                k1_i1: FlowAt(TimeLevel, i + 1), k1_i: FlowAt(TimeLevel, i),
                k_i1: FlowAt(TimeLevel - 1, i + 1), k_i: FlowAt(TimeLevel - 1, i));

            return dA_dt + dQ_dx;
        }

        /// <summary>
        /// 单元 i 的动量方程残差。
        /// <para>
        /// 圣维南动量方程：∂Q/∂t + ∂(Q²/A)/∂x + g·A·(∂Y/∂x + Se) = 0。
        /// 各项均采用 Preissmann 时空加权离散。
        /// 其中 Se = Sf + Sc（摩阻坡度 + 弯曲坡度）。
        /// </para>
        /// </summary>
        private double MomentumResidual(int i)
        {
            // 提取两个时层、两个节点的 A 和 Q 值（共 8 个）
            double A_k_i   = AreaAt(TimeLevel - 1, i),     A_k_i1  = AreaAt(TimeLevel - 1, i + 1);
            double A_k1_i  = AreaAt(TimeLevel, i),          A_k1_i1 = AreaAt(TimeLevel, i + 1);
            double Q_k_i   = FlowAt(TimeLevel - 1, i),     Q_k_i1  = FlowAt(TimeLevel - 1, i + 1);
            double Q_k1_i  = FlowAt(TimeLevel, i),          Q_k1_i1 = FlowAt(TimeLevel, i + 1);

            // ∂Q/∂t：时间导数（以 Q 的 Preissmann 加权平均差分）
            double dQ_dt = TimeDiff(k1_i1: Q_k1_i1, k1_i: Q_k1_i, k_i1: Q_k_i1, k_i: Q_k_i);

            // ∂(Q²/A)/∂x：对流项（动量通量的空间导数）
            double dQ2A_dx = SpatialDiff(
                k1_i1: Q_k1_i1 * Q_k1_i1 / Math.Max(A_k1_i1, 1e-6),
                k1_i:  Q_k1_i  * Q_k1_i  / Math.Max(A_k1_i, 1e-6),
                k_i1:  Q_k_i1  * Q_k_i1  / Math.Max(A_k_i1, 1e-6),
                k_i:   Q_k_i   * Q_k_i   / Math.Max(A_k_i, 1e-6));

            // 单元平均过水面积（用于重力项系数 g·avg_A）
            double avg_A = CellAvg(k1_i1: A_k1_i1, k1_i: A_k1_i, k_i1: A_k_i1, k_i: A_k_i);

            // ∂Y/∂x：水面坡度（绝对水位的空间导数，含床底变化）
            double dY_dx = SpatialDiff(
                k1_i1: WaterLevelAt(TimeLevel, i + 1),
                k1_i:  WaterLevelAt(TimeLevel, i),
                k_i1:  WaterLevelAt(TimeLevel - 1, i + 1),
                k_i:   WaterLevelAt(TimeLevel - 1, i));

            // 单元平均等效能量坡度 Se（摩阻 + 弯曲）
            double avg_Se = CellAvg(
                k1_i1: SeAt(TimeLevel, i + 1), k1_i: SeAt(TimeLevel, i),
                k_i1:  SeAt(TimeLevel - 1, i + 1), k_i: SeAt(TimeLevel - 1, i));

            // 动量方程：dQ/dt + dQ²/A_dx + g·avg_A·(dY_dx + avg_Se) = 0
            return dQ_dt + dQ2A_dx + Hydraulics.G * avg_A * (dY_dx + avg_Se);
        }

        // ---- 雅可比矩阵各分量 ----

        /// <summary>上游边界残差对 h_0 的偏导 ∂R_0/∂h_0。</summary>
        private double DU_Dh()
        {
            double h = DepthAt(TimeLevel, 0), Q = FlowAt(TimeLevel, 0);
            double t = TimeLevel * TimeStep;
            return Channel.UpstreamBoundary.Df_Dh(h, Q, t);
        }

        /// <summary>上游边界残差对 Q_0 的偏导 ∂R_0/∂Q_0。</summary>
        private double DU_DQ()
        {
            double h = DepthAt(TimeLevel, 0), Q = FlowAt(TimeLevel, 0);
            double t = TimeLevel * TimeStep;
            double vol = 0.5 * (Q + FlowAt(TimeLevel - 1, 0));  // 注：此处 vol 为流量，不乘 dt
            return Channel.UpstreamBoundary.Df_DQ(h, Q, TimeStep, t, vol);
        }

        /// <summary>下游边界残差对 h_{N-1} 的偏导。</summary>
        private double DD_Dh()
        {
            double h = DepthAt(TimeLevel, -1), Q = FlowAt(TimeLevel, -1);
            double t = TimeLevel * TimeStep;
            return Channel.DownstreamBoundary.Df_Dh(h, Q, t);
        }

        /// <summary>下游边界残差对 Q_{N-1} 的偏导。</summary>
        private double DD_DQ()
        {
            double h = DepthAt(TimeLevel, -1), Q = FlowAt(TimeLevel, -1);
            double t = TimeLevel * TimeStep;
            double vol = 0.5 * (Q + FlowAt(TimeLevel - 1, -1)) * TimeStep;
            return Channel.DownstreamBoundary.Df_DQ(h, Q, TimeStep, t, vol);
        }

        // ---- 连续性方程雅可比分量 ----
        // 连续性残差 R_c = dA_dt + dQ_dx，各分量由 TimeDiff/SpatialDiff 运算符的线性性导出。

        /// <summary>∂R_c/∂h_i = (∂TimeDiff/∂A_i) * (dA/dh)_i。</summary>
        private double DC_Dh_i(int i)
            => TimeDiff(k1_i: 1) * DADhAt(TimeLevel, i);   // TimeDiff(k1_i=1) = 1/(2dt)

        /// <summary>∂R_c/∂Q_i = ∂SpatialDiff/∂Q_i = -θ/Δx 或 -(1-θ)/Δx（取决于时层）。</summary>
        private double DC_DQ_i(int i)
            => SpatialDiff(k1_i: 1);   // SpatialDiff(k1_i=1) = -θ/Δx

        /// <summary>∂R_c/∂h_{i+1} = (∂TimeDiff/∂A_{i+1}) * (dA/dh)_{i+1}。</summary>
        private double DC_Dh_ip1(int i)
            => TimeDiff(k1_i1: 1) * DADhAt(TimeLevel, i + 1);

        /// <summary>∂R_c/∂Q_{i+1} = ∂SpatialDiff/∂Q_{i+1} = θ/Δx。</summary>
        private double DC_DQ_ip1(int i)
            => SpatialDiff(k1_i1: 1);

        // ---- 动量方程雅可比分量（更为复杂，需链式法则展开各非线性项）----

        /// <summary>动量残差对节点 i 水深的偏导 ∂R_m/∂h_i。</summary>
        private double DM_Dh_i(int i)
        {
            double A = AreaAt(TimeLevel, i), Q = FlowAt(TimeLevel, i), h = DepthAt(TimeLevel, i);
            double dA_dh  = DADhAt(TimeLevel, i);       // dA/dh = T（水面宽）
            double dSe_dA = Channel.DSe_DA(h, Q, i);   // ∂Se/∂A（摩阻 + 弯曲）

            // 单元平均量（用于链式法则展开重力项）
            double avg_A = CellAvg(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));
            double dY_dx = SpatialDiff(
                k1_i1: WaterLevelAt(TimeLevel, i + 1), k1_i: WaterLevelAt(TimeLevel, i),
                k_i1: WaterLevelAt(TimeLevel - 1, i + 1), k_i: WaterLevelAt(TimeLevel - 1, i));
            double avg_Se = CellAvg(
                k1_i1: SeAt(TimeLevel, i + 1), k1_i: SeAt(TimeLevel, i),
                k_i1: SeAt(TimeLevel - 1, i + 1), k_i: SeAt(TimeLevel - 1, i));

            // 对流项 Q²/A 对 A 的偏导（注意 A 通过 h 变化，故需乘 dA/dh）
            double d_dQ2Adx_dA = -SpatialDiff(k1_i: 1) * (Q / Math.Max(A, 1e-6)) * (Q / Math.Max(A, 1e-6));
            // 重力项各分量对 A_i 的偏导
            double d_avgA_dA   = CellAvg(k1_i: 1);          // ∂avg_A/∂A_i
            double d_dYdx_dh   = SpatialDiff(k1_i: 1);      // ∂(dY_dx)/∂h_i（水位包含床底+水深）
            double d_avgSe_dA  = CellAvg(k1_i: 1) * dSe_dA; // ∂avg_Se/∂A_i

            return (d_dQ2Adx_dA + Hydraulics.G * (
                avg_A * (d_dYdx_dh + d_avgSe_dA * dA_dh) +
                d_avgA_dA * dA_dh * (dY_dx + avg_Se))) * dA_dh;
        }

        /// <summary>动量残差对节点 i 流量的偏导 ∂R_m/∂Q_i。</summary>
        private double DM_DQ_i(int i)
        {
            double A = AreaAt(TimeLevel, i), Q = FlowAt(TimeLevel, i), h = DepthAt(TimeLevel, i);
            double dSe_dQ = Channel.DSe_DQ(h, Q, i);   // ∂Se/∂Q

            double avg_A = CellAvg(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));

            double d_dQdt_dQ   = TimeDiff(k1_i: 1);                          // ∂(dQ_dt)/∂Q_i
            double d_dQ2Adx_dQ = SpatialDiff(k1_i: 1) * 2.0 * Q / Math.Max(A, 1e-6); // ∂(dQ²/A_dx)/∂Q_i
            double d_avgSe_dQ  = CellAvg(k1_i: 1) * dSe_dQ;                // ∂avg_Se/∂Q_i

            return d_dQdt_dQ + d_dQ2Adx_dQ + Hydraulics.G * avg_A * d_avgSe_dQ;
        }

        /// <summary>动量残差对节点 i+1 水深的偏导 ∂R_m/∂h_{i+1}。</summary>
        private double DM_Dh_ip1(int i)
        {
            double A = AreaAt(TimeLevel, i + 1), Q = FlowAt(TimeLevel, i + 1), h = DepthAt(TimeLevel, i + 1);
            double dA_dh  = DADhAt(TimeLevel, i + 1);
            double dSe_dA = Channel.DSe_DA(h, Q, i + 1);

            double avg_A = CellAvg(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));
            double dY_dx = SpatialDiff(
                k1_i1: WaterLevelAt(TimeLevel, i + 1), k1_i: WaterLevelAt(TimeLevel, i),
                k_i1: WaterLevelAt(TimeLevel - 1, i + 1), k_i: WaterLevelAt(TimeLevel - 1, i));
            double avg_Se = CellAvg(
                k1_i1: SeAt(TimeLevel, i + 1), k1_i: SeAt(TimeLevel, i),
                k_i1: SeAt(TimeLevel - 1, i + 1), k_i: SeAt(TimeLevel - 1, i));

            double d_dQ2Adx_dA = -SpatialDiff(k1_i1: 1) * (Q / Math.Max(A, 1e-6)) * (Q / Math.Max(A, 1e-6));
            double d_avgA_dA   = CellAvg(k1_i1: 1);
            double d_dYdx_dh   = SpatialDiff(k1_i1: 1);
            double d_avgSe_dA  = CellAvg(k1_i1: 1) * dSe_dA;

            return (d_dQ2Adx_dA + Hydraulics.G * (
                avg_A * (d_dYdx_dh + d_avgSe_dA * dA_dh) +
                d_avgA_dA * dA_dh * (dY_dx + avg_Se))) * dA_dh;
        }

        /// <summary>动量残差对节点 i+1 流量的偏导 ∂R_m/∂Q_{i+1}。</summary>
        private double DM_DQ_ip1(int i)
        {
            double A = AreaAt(TimeLevel, i + 1), Q = FlowAt(TimeLevel, i + 1), h = DepthAt(TimeLevel, i + 1);
            double dSe_dQ = Channel.DSe_DQ(h, Q, i + 1);

            double avg_A = CellAvg(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));

            double d_dQdt_dQ   = TimeDiff(k1_i1: 1);
            double d_dQ2Adx_dQ = SpatialDiff(k1_i1: 1) * 2.0 * Q / Math.Max(A, 1e-6);
            double d_avgSe_dQ  = CellAvg(k1_i1: 1) * dSe_dQ;

            return d_dQdt_dQ + d_dQ2Adx_dQ + Hydraulics.G * avg_A * d_avgSe_dQ;
        }

        // ---- Preissmann 有限差分算子（与 Python preissmann.py 保持完全一致）----

        /// <summary>
        /// 时间差分算子（计算两时层、两节点平均值的差）。
        /// 公式：(k1_i1 + k1_i - k_i1 - k_i) / (2·Δt)
        /// 等价于：(新时层均值 - 旧时层均值) / Δt，其中均值 = 0.5*(节点 i + 节点 i+1)。
        /// </summary>
        /// <param name="k1_i1">新时层节点 i+1 处的值（默认 0）。</param>
        /// <param name="k1_i">新时层节点 i 处的值（默认 0）。</param>
        /// <param name="k_i1">旧时层节点 i+1 处的值（默认 0）。</param>
        /// <param name="k_i">旧时层节点 i 处的值（默认 0）。</param>
        private double TimeDiff(double k1_i1 = 0, double k1_i = 0, double k_i1 = 0, double k_i = 0)
            => (k1_i1 + k1_i - k_i1 - k_i) / (2.0 * TimeStep);

        /// <summary>
        /// Preissmann 加权空间差分算子。
        /// 公式：θ·(k1_i1 - k1_i)/Δx + (1-θ)·(k_i1 - k_i)/Δx
        /// θ 越大，隐式程度越强（θ=1 为全隐式）。
        /// </summary>
        /// <param name="k1_i1">新时层节点 i+1 处的值。</param>
        /// <param name="k1_i">新时层节点 i 处的值。</param>
        /// <param name="k_i1">旧时层节点 i+1 处的值。</param>
        /// <param name="k_i">旧时层节点 i 处的值。</param>
        private double SpatialDiff(double k1_i1 = 0, double k1_i = 0, double k_i1 = 0, double k_i = 0)
        {
            double dx_k1 = (k1_i1 - k1_i) / SpatialStep;   // 新时层空间导数
            double dx_k  = (k_i1  - k_i)  / SpatialStep;   // 旧时层空间导数
            return Theta * dx_k1 + (1.0 - Theta) * dx_k;   // Preissmann 加权
        }

        /// <summary>
        /// Preissmann 加权单元均值算子。
        /// 公式：0.5·θ·(k1_i1 + k1_i) + 0.5·(1-θ)·(k_i1 + k_i)
        /// 即新旧时层空间平均值的时间加权。
        /// </summary>
        private double CellAvg(double k1_i1 = 0, double k1_i = 0, double k_i1 = 0, double k_i = 0)
            => 0.5 * Theta * (k1_i1 + k1_i) + 0.5 * (1.0 - Theta) * (k_i1 + k_i);
    }
}
