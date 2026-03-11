using System;

namespace FlowSim.Models
{
    /// <summary>
    /// 边界条件类型枚举。
    /// <list type="bullet">
    ///   <item><term>FlowHydrograph</term><description>流量过程线边界：随时间给定流量 Q(t)。</description></item>
    ///   <item><term>FixedDepth</term><description>固定水深边界：给定恒定水深，或通过集总调蓄库动态计算。</description></item>
    ///   <item><term>NormalDepth</term><description>正常水深边界：由曼宁公式计算对应坡度下的均匀流水深。</description></item>
    ///   <item><term>RatingCurve</term><description>水位流量关系曲线边界：由曲线查取流量。</description></item>
    ///   <item><term>StageHydrograph</term><description>水位过程线边界：随时间给定水位 Z(t)。</description></item>
    /// </list>
    /// </summary>
    public enum BoundaryConditionType
    {
        FlowHydrograph,   // 流量过程线
        FixedDepth,       // 固定水深（或集总调蓄）
        NormalDepth,      // 正常水深（均匀流）
        RatingCurve,      // 水位-流量关系曲线
        StageHydrograph   // 水位过程线
    }

    /// <summary>
    /// 表示河道上游或下游的边界条件。
    /// 包含边界处的地形信息（桩号、床底高程）、初始条件、
    /// 以及对应边界类型所需的过程线或关系曲线数据。
    /// <para>
    /// 边界残差 <see cref="ConditionResidual"/> 和雅可比偏导数
    /// <see cref="Df_Dh"/>/<see cref="Df_DQ"/> 由 Preissmann 隐式求解器调用，
    /// 用于构建非线性方程组。
    /// </para>
    /// </summary>
    public class Boundary
    {
        /// <summary>边界条件类型（流量过程线、固定水深等）。</summary>
        public BoundaryConditionType Condition { get; }

        /// <summary>边界处所对应的水力断面（由河道初始化时注入）。</summary>
        public CrossSection? CrossSection { get; set; }

        /// <summary>床底高程（m），用于将水深转换为绝对水位。</summary>
        public double? BedLevel { get; }

        /// <summary>初始水深（m），用于 FixedDepth 边界或初始化时赋值。</summary>
        public double? InitialDepth { get; set; }

        /// <summary>初始水位（m）= BedLevel + InitialDepth，仅当二者均已知时有值。</summary>
        public double? InitialStage { get; }

        /// <summary>边界桩号（m），沿河道纵轴距起点的距离。</summary>
        public double Chainage { get; }

        /// <summary>水位-流量关系曲线，仅 RatingCurve 类型时使用。</summary>
        public RatingCurve? RatingCurve { get; }

        /// <summary>流量或水位过程线，FlowHydrograph 和 StageHydrograph 类型时使用。</summary>
        public Hydrograph? Hydrograph { get; }

        /// <summary>集总调蓄库（如水库），FixedDepth 类型可选关联。</summary>
        public LumpedStorage? LumpedStorage { get; private set; }

        /// <summary>
        /// 构造函数：初始化边界条件的所有参数。
        /// </summary>
        /// <param name="condition">边界条件类型。</param>
        /// <param name="chainage">桩号（m），沿河道方向的距离。</param>
        /// <param name="bedLevel">床底高程（m）。</param>
        /// <param name="initialDepth">初始水深（m）。</param>
        /// <param name="ratingCurve">水位-流量关系曲线（可选）。</param>
        /// <param name="hydrograph">过程线（可选，FlowHydrograph/StageHydrograph 时须传入）。</param>
        public Boundary(BoundaryConditionType condition, double chainage,
                        double? bedLevel = null, double? initialDepth = null,
                        RatingCurve? ratingCurve = null, Hydrograph? hydrograph = null)
        {
            Condition = condition;
            Chainage = chainage;
            BedLevel = bedLevel;
            InitialDepth = initialDepth;
            // 当床底高程和初始水深均已知时，计算初始水位
            InitialStage = bedLevel.HasValue && initialDepth.HasValue ? bedLevel + initialDepth : null;
            RatingCurve = ratingCurve;
            Hydrograph = hydrograph;
        }

        /// <summary>
        /// 关联集总调蓄库到本边界（仅 FixedDepth 类型有效）。
        /// </summary>
        /// <param name="ls">集总调蓄库实例。</param>
        public void SetLumpedStorage(LumpedStorage ls) => LumpedStorage = ls;

        /// <summary>
        /// 判断该边界方程是否以流量 Q 为未知量。
        /// 若为 true，残差形式为 Q - target；若为 false，残差形式为 depth - target。
        /// FlowHydrograph、NormalDepth 和 RatingCurve 均以流量为约束目标。
        /// </summary>
        public bool IsFlowDependent =>
            Condition == BoundaryConditionType.FlowHydrograph ||
            Condition == BoundaryConditionType.NormalDepth ||
            Condition == BoundaryConditionType.RatingCurve;

        /// <summary>
        /// 计算边界条件的残差 f = unknown - target。
        /// <para>
        /// Preissmann 方案在每个牛顿迭代步中调用此方法，
        /// 要求残差为零时满足边界约束。
        /// </para>
        /// </summary>
        /// <param name="depth">当前迭代的水深 h（m）。</param>
        /// <param name="flow">当前迭代的流量 Q（m³/s）。</param>
        /// <param name="time">当前时刻（秒），用于时变边界。</param>
        /// <param name="duration">时间步长（秒），集总调蓄库质量守恒计算时使用。</param>
        /// <param name="volIn">本时步内流入体积（m³），集总调蓄库使用。</param>
        /// <returns>残差值；当约束被满足时残差为 0。</returns>
        public double ConditionResidual(double depth, double flow, double time = 0,
                                        double duration = 0, double volIn = 0)
        {
            if (CrossSection == null) throw new InvalidOperationException("Cross section not set.");

            double hw = CrossSection.ZMin + depth;   // 绝对水位（m）= 床底高程 + 水深
            double S0 = CrossSection.BedSlope ?? 0;  // 河床纵坡

            // 根据边界类型确定"未知量"是流量还是水深
            double unknown = IsFlowDependent ? flow : depth;
            double target;

            switch (Condition)
            {
                case BoundaryConditionType.FlowHydrograph:
                    // 目标：从过程线读取当前时刻的流量
                    if (Hydrograph == null) throw new InvalidOperationException("Hydrograph not set.");
                    target = Hydrograph.GetAt(time);
                    break;

                case BoundaryConditionType.NormalDepth:
                    // 目标：利用曼宁公式计算正常流量 Qn = K * sqrt(S0)
                    target = Hydraulics.NormalFlow(S0, CrossSection.Conveyance(hw));
                    break;

                case BoundaryConditionType.RatingCurve:
                    // 目标：从水位-流量关系曲线查取对应流量
                    if (RatingCurve == null) throw new InvalidOperationException("Rating curve not set.");
                    target = RatingCurve.Discharge((BedLevel ?? 0) + depth, time);
                    break;

                case BoundaryConditionType.FixedDepth:
                    if (LumpedStorage == null)
                    {
                        // 无调蓄库：目标水深直接取初始深度
                        target = InitialDepth ?? 0;
                    }
                    else
                    {
                        // 有调蓄库：通过质量守恒方程（MassBalance）动态计算库水位，
                        // 并加上因流速引起的能量损失（headLoss），得到界面水深
                        if (duration <= 0) throw new ArgumentException("Duration must be positive for lumped storage boundary.");

                        int k = (int)(time / duration);  // 当前时步序号

                        // 获取上一时步的水位（用于质量守恒迭代的初始值）
                        double yOld;
                        if (k == 1)
                            yOld = depth + (BedLevel ?? 0);
                        else if (k >= 2 && LumpedStorage.StageHydrograph.Count >= k - 1)
                            yOld = LumpedStorage.StageHydrograph[k - 2][1];
                        else
                            yOld = depth + (BedLevel ?? 0);

                        // 调用集总调蓄质量守恒，求解库水位
                        double reservoirStage = LumpedStorage.MassBalance(duration, volIn, yOld, time);

                        // 计算连接断面处的能量损失（摩阻损失 + 扩散损失 + 经验损失）
                        double n = CrossSection.GetEquivalentN(hw);
                        double R = CrossSection.HydraulicRadius(hw);
                        double area = CrossSection.Area(hw);
                        double headLoss = LumpedStorage.EnergyLoss(area, flow, n, R);

                        // 界面水位 = 库水位 + 能量损失
                        double interfaceStage = reservoirStage + headLoss;

                        // 更新/追加库水位历史（供后续时步使用）
                        if (LumpedStorage.StageHydrograph.Count == 0)
                            LumpedStorage.StageHydrograph.Add(new[] { time, reservoirStage });
                        else if (Math.Abs(LumpedStorage.StageHydrograph[^1][0] - time) < 1e-9)
                            LumpedStorage.StageHydrograph[^1][1] = reservoirStage;
                        else
                            LumpedStorage.StageHydrograph.Add(new[] { time, reservoirStage });

                        // 目标水深 = 界面水位 - 床底高程
                        target = interfaceStage - (BedLevel ?? 0);
                    }
                    break;

                case BoundaryConditionType.StageHydrograph:
                    // 目标水深 = 给定水位 - 床底高程
                    if (Hydrograph == null) throw new InvalidOperationException("Hydrograph not set.");
                    target = Hydrograph.GetAt(time) - (BedLevel ?? 0);
                    break;

                default:
                    throw new InvalidOperationException("Invalid boundary condition type.");
            }

            // 残差 = 当前估算值 - 目标值；牛顿迭代中要求此值趋向 0
            return unknown - target;
        }

        /// <summary>
        /// 计算边界残差对水深 h 的偏导数 ∂f/∂h，用于构造雅可比矩阵。
        /// 当边界条件以流量为约束时（IsFlowDependent=true），对应列系数由 <see cref="Df_DQ"/> 给出，
        /// 此处 FlowHydrograph 类型直接返回 0。
        /// </summary>
        /// <param name="depth">当前水深（m）。</param>
        /// <param name="flowRate">当前流量（m³/s）。</param>
        /// <param name="time">当前时刻（秒）。</param>
        /// <returns>∂f/∂h 的值。</returns>
        public double Df_Dh(double depth, double flowRate, double time = 0)
        {
            // FlowHydrograph：残差 = Q - target，与 h 无关，偏导为 0
            if (Condition == BoundaryConditionType.FlowHydrograph) return 0;
            if (CrossSection == null) throw new InvalidOperationException("Cross section not set.");

            double hw = depth + (BedLevel ?? CrossSection.ZMin);   // 绝对水位
            double dA_dh = CrossSection.DArea_Dh(hw);             // 水面宽（过水面积对水深的导数）
            double S0 = CrossSection.BedSlope ?? 0;

            switch (Condition)
            {
                case BoundaryConditionType.FixedDepth:
                    if (LumpedStorage != null)
                    {
                        // 有调蓄库时，∂f/∂h = 1 - ∂(headLoss)/∂A * dA/dh
                        double R = CrossSection.HydraulicRadius(hw);
                        double n = CrossSection.GetEquivalentN(hw);
                        double dR_dA = CrossSection.DRadius_DA(hw);
                        double area = CrossSection.Area(hw);
                        double dhl_dA = LumpedStorage.Dhl_DA(area, flowRate, n, R, dR_dA);
                        return 1.0 - dhl_dA * dA_dh;
                    }
                    // 无调蓄库：∂f/∂h = 1（目标水深为常数，残差 = h - target）
                    return 1.0;

                case BoundaryConditionType.NormalDepth:
                    // 残差 = Q - K*sqrt(S0)，对 h 的偏导通过链式法则：
                    // ∂f/∂h = -∂Qn/∂A * dA/dh
                    double dK_dA = CrossSection.DConveyance_DA(hw);
                    return -Hydraulics.DNormalFlow_DA(S0, dK_dA) * dA_dh;

                case BoundaryConditionType.RatingCurve:
                    // 残差 = Q - Q(Z)，对 h 的偏导 = -dQ/dZ
                    if (RatingCurve == null) return 0;
                    return -RatingCurve.DQ_Dz((BedLevel ?? 0) + depth, time);

                case BoundaryConditionType.StageHydrograph:
                    // 残差 = h - target，∂f/∂h = 1
                    return 1.0;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// 计算边界残差对流量 Q 的偏导数 ∂f/∂Q，用于构造雅可比矩阵。
        /// </summary>
        /// <param name="depth">当前水深（m）。</param>
        /// <param name="flowRate">当前流量（m³/s）。</param>
        /// <param name="duration">时间步长（秒），集总调蓄库使用。</param>
        /// <param name="time">当前时刻（秒）。</param>
        /// <param name="volIn">本时步流入体积（m³），集总调蓄库使用。</param>
        /// <returns>∂f/∂Q 的值。</returns>
        public double Df_DQ(double depth, double flowRate, double duration = 0, double time = 0, double volIn = 0)
        {
            // 以流量为约束目标时（FlowHydrograph/NormalDepth/RatingCurve），残差 = Q - target，∂f/∂Q = 1
            if (IsFlowDependent) return 1.0;

            if (CrossSection == null) throw new InvalidOperationException("Cross section not set.");
            double hw = depth + (BedLevel ?? CrossSection.ZMin);   // 绝对水位

            switch (Condition)
            {
                case BoundaryConditionType.FixedDepth:
                    if (LumpedStorage != null)
                    {
                        // 库水位通过质量守恒依赖于流入体积 volIn，volIn 又依赖 Q，
                        // 故 ∂f/∂Q = -(∂Ynew/∂volIn * dvol/dQ + ∂headLoss/∂Q)
                        int k = (int)(time / Math.Max(duration, 1e-10));
                        double yOld;
                        if (k == 1 || LumpedStorage.StageHydrograph.Count < k - 1)
                            yOld = depth + (BedLevel ?? 0);
                        else
                            yOld = LumpedStorage.StageHydrograph[k - 2][1];

                        double dY_dvol = LumpedStorage.DYnew_DvolIn(duration, volIn, yOld, time);
                        double dvol_dQ = 0.5 * duration;  // volIn = 0.5*(Q_old+Q_new)*dt，对 Q_new 的导数
                        double R = CrossSection.HydraulicRadius(hw);
                        double n = CrossSection.GetEquivalentN(hw);
                        double area = CrossSection.Area(hw);
                        double dhl_dQ = LumpedStorage.Dhl_DQ(area, flowRate, n, R);
                        return -(dY_dvol * dvol_dQ + dhl_dQ);
                    }
                    // 无调蓄库：目标为常数水深，与 Q 无关，偏导为 0
                    return 0;

                case BoundaryConditionType.StageHydrograph:
                    // 残差 = h - target，与 Q 无关，偏导为 0
                    return 0;

                default:
                    return 0;
            }
        }
    }
}
