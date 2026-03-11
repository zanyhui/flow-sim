using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowSim.Models
{
    /// <summary>
    /// 集总调蓄库（零维水库）模型。
    /// <para>
    /// 将水库或滞洪区简化为"集总"（lumped）单元，通过质量守恒方程计算水位变化，
    /// 可选考虑出口处的能量损失（摩阻损失、扩散损失和经验损失）。
    /// <br/>
    /// 典型用途：作为下游边界 FixedDepth 的补充，
    /// 由 <see cref="Boundary.ConditionResidual"/> 调用以动态计算边界水深。
    /// </para>
    /// <para>
    /// 质量守恒方程（每个时步）：
    /// ΔStorage = V_in - Q_out·Δt
    /// 其中 V_in 为本时步流入体积（m³），Q_out 为出口平均流量（由 RatingCurve 给出）。
    /// 通过 Brent 方法求解新水位 y_new 满足此方程。
    /// </para>
    /// </summary>
    public class LumpedStorage
    {
        /// <summary>出口水位-流量关系曲线（可选；无则假设无出流）。</summary>
        public RatingCurve? RatingCurve { get; set; }

        /// <summary>水库等效水面面积（m²，当无面积曲线时使用常数值）。</summary>
        public double? SurfaceArea { get; set; }

        /// <summary>最低允许水位（m，若算得水位低于此值则夹紧）。</summary>
        public double? MinStage { get; set; }

        /// <summary>水位过程线历史记录，格式为 List&lt;{time, stage}&gt;，供边界计算使用。</summary>
        public List<double[]> StageHydrograph { get; } = new();

        /// <summary>面积-水位关系曲线 [n, 2]（第 0 列为水位 m，第 1 列为面积 m²）。</summary>
        public double[,]? AreaCurve { get; private set; }

        /// <summary>是否考虑能量损失（摩阻/扩散/经验）；默认 false。</summary>
        public bool CaptureLosses { get; set; } = false;

        /// <summary>能量损失系数 Cc（用于扩散损失计算，默认 0.5）。</summary>
        public double Cc { get; set; } = 0.5;

        /// <summary>经验能量损失系数 KQ（用于 EmpiricalLoss，默认 0）。</summary>
        public double KQ { get; set; } = 0;

        /// <summary>水库有效长度（m，用于摩阻损失计算）。</summary>
        public double? ReservoirLength { get; set; }

        private double _yMin, _yMax;    // 水位搜索范围（m）
        private double _alpha = 1, _beta = 0;   // 面积曲线缩放系数（α·A + β）
        private double[]? _areaCurveStages;      // 面积曲线水位列（插值用）
        private double[]? _areaCurveAreas;       // 面积曲线面积列（插值用）

        /// <summary>
        /// 构造集总调蓄库对象。
        /// </summary>
        /// <param name="yMin">水位搜索下限（m），通常为最低库底高程。</param>
        /// <param name="yMax">水位搜索上限（m），通常为防洪限制水位。</param>
        /// <param name="surfaceArea">等效水面面积（m²，无面积曲线时使用）。</param>
        /// <param name="minStage">最低水位限制（m，可选）。</param>
        /// <param name="ratingCurve">出口水位-流量关系曲线（可选）。</param>
        public LumpedStorage(double yMin, double yMax, double? surfaceArea = null,
                              double? minStage = null, RatingCurve? ratingCurve = null)
        {
            _yMin = yMin; _yMax = yMax;
            SurfaceArea = surfaceArea;
            MinStage = minStage;
            RatingCurve = ratingCurve;
        }

        /// <summary>
        /// 求解本时步的库水位（质量守恒）。
        /// <para>
        /// 方程：NetVolChange(yOld, yNew) = V_in - Q_out_avg·dt
        /// 其中 Q_out_avg = 0.5·(Q_out(yOld) + Q_out(yNew))（梯形近似）。
        /// 用 Brent 方法在 [_yMin, _yMax] 内求解 yNew。
        /// </para>
        /// </summary>
        /// <param name="duration">时间步长（秒）。</param>
        /// <param name="volIn">本时步流入体积（m³）。</param>
        /// <param name="yOld">上一时步末的库水位（m）。</param>
        /// <param name="time">当前时刻（秒，用于时变出口曲线）。</param>
        /// <returns>本时步末的新库水位 yNew（m）。</returns>
        public double MassBalance(double duration, double volIn, double yOld, double time = 0)
        {
            double F(double yNew)
            {
                // 出口流量取新旧水位的平均（梯形近似，二阶精度）
                double qOut = RatingCurve != null
                    ? 0.5 * (RatingCurve.Discharge(yOld, time) + RatingCurve.Discharge(yNew, time))
                    : 0.0;
                double targetVol = volIn - qOut * duration;   // 目标净蓄水量变化
                return NetVolChange(yOld, yNew) - targetVol;  // 残差
            }
            double yTarget;
            try
            {
                yTarget = Hydraulics.Brentq(F, _yMin, _yMax);
            }
            catch (ArgumentException)
            {
                // Brentq 要求区间两端函数值异号。若不满足（例如极端入流或水位已触及搜索边界），
                // 则夹紧到最近端点：F(_yMin)<0 说明目标蓄量过大，水位取上限；否则取下限。
                yTarget = F(_yMin) < 0 ? _yMax : _yMin;
            }
            // 应用最低水位限制
            if (MinStage.HasValue && yTarget < MinStage.Value) yTarget = MinStage.Value;
            return yTarget;
        }

        /// <summary>
        /// 计算新库水位对流入体积的导数 dY_new/dV_in。
        /// 由隐函数定理推导：dY_new/dV_in = 1 / A(Y_new)，
        /// 其中 A(Y_new) 为对应水位的水库面积。
        /// 若已触及最低水位限制，导数为 0（水位不再变化）。
        /// </summary>
        /// <param name="duration">时间步长（秒）。</param>
        /// <param name="volIn">流入体积（m³）。</param>
        /// <param name="yOld">上一时步水位（m）。</param>
        /// <param name="time">当前时刻（秒）。</param>
        /// <returns>dY_new/dV_in（1/m²）。</returns>
        public double DYnew_DvolIn(double duration, double volIn, double yOld, double time = 0)
        {
            double yNew = MassBalance(duration, volIn, yOld, time);
            if (MinStage.HasValue && yNew <= MinStage.Value) return 0.0;  // 触及底部限制
            return 1.0 / AreaAt(yNew);
        }

        /// <summary>
        /// 计算连接口处的总能量损失（摩阻 + 扩散 + 经验）。
        /// 若 CaptureLosses = false，直接返回 0。
        /// </summary>
        /// <param name="entryArea">进口断面过水面积（m²）。</param>
        /// <param name="flow">流量（m³/s）。</param>
        /// <param name="roughness">进口断面等效曼宁糙率。</param>
        /// <param name="hydraulicRadius">进口断面水力半径（m）。</param>
        /// <param name="aStr">水库内水面面积（m²，可选，用于扩散损失计算）。</param>
        /// <returns>总能量损失（m）。</returns>
        public double EnergyLoss(double entryArea, double flow, double roughness, double hydraulicRadius, double? aStr = null)
        {
            if (!CaptureLosses) return 0.0;
            double hf   = FrictionLoss(entryArea, flow, roughness, hydraulicRadius);     // 摩阻损失
            double hExp = ExpansionLoss(entryArea, flow, aStr);                           // 扩散损失
            double hEmp = EmpiricalLoss(entryArea, flow);                                 // 经验损失
            return hf + hExp + hEmp;
        }

        /// <summary>
        /// 摩阻损失 hf = Sf · L，其中 Sf = Q²/K²（曼宁公式），L = ReservoirLength。
        /// </summary>
        private double FrictionLoss(double aEnt, double Q, double n, double R)
            => Hydraulics.FrictionSlope(Q, Hydraulics.Conveyance(aEnt, n, R)) * (ReservoirLength ?? 0);

        /// <summary>
        /// 扩散（突扩）损失 hExp = K·V²/(2g)，K = (1 - A_入口/A_水库)²。
        /// 模拟水流从窄口进入宽库时的动能耗散。
        /// </summary>
        private double ExpansionLoss(double aEnt, double Q, double? aStr)
        {
            if (!aStr.HasValue) return 0;
            double K = Math.Pow(1.0 - aEnt / aStr.Value, 2.0);  // 损失系数
            double V = Q / aEnt;                                  // 进口流速（m/s）
            return K * V * V / (2 * Hydraulics.G);
        }

        /// <summary>
        /// 经验损失 hEmp = KQ·V²/(2g)，KQ 由用户指定（默认 0）。
        /// </summary>
        private double EmpiricalLoss(double aEnt, double Q)
        {
            double V = Q / aEnt;
            return KQ * V * V / (2 * Hydraulics.G);
        }

        /// <summary>
        /// 计算总能量损失对进口过水面积的偏导数 ∂h_L/∂A（用于雅可比矩阵）。
        /// 若 CaptureLosses = false，直接返回 0。
        /// </summary>
        /// <param name="entryArea">进口面积（m²）。</param>
        /// <param name="flow">流量（m³/s）。</param>
        /// <param name="roughness">曼宁糙率。</param>
        /// <param name="hydraulicRadius">水力半径（m）。</param>
        /// <param name="dR_dA">dR/dA（水力半径对面积的导数）。</param>
        /// <param name="aStr">库内水面面积（m²，可选）。</param>
        /// <returns>∂h_L/∂A 的值。</returns>
        public double Dhl_DA(double entryArea, double flow, double roughness, double hydraulicRadius, double dR_dA, double? aStr = null)
        {
            if (!CaptureLosses) return 0.0;

            // 摩阻损失对 A 的偏导：∂hf/∂A = ∂Sf/∂A · L
            double K   = Hydraulics.Conveyance(entryArea, roughness, hydraulicRadius);
            double dK  = Hydraulics.DConveyance_DA(entryArea, roughness, hydraulicRadius, dR_dA);
            double dhf_dA = Hydraulics.DFrictionSlope_DA(flow, K, dK) * (ReservoirLength ?? 0);

            // 经验损失对 A 的偏导：∂hEmp/∂A = KQ·2V·(dV/dA)/(2g)
            double V = flow / entryArea;
            double dV_dA = -flow / (entryArea * entryArea);    // dV/dA = -Q/A²
            double dhEmp_dA = KQ * 2 * V * dV_dA / (2 * Hydraulics.G);

            // 扩散损失对 A 的偏导（商法则展开）
            double dhExp_dA = 0;
            if (aStr.HasValue)
            {
                double Kc    = Math.Pow(1.0 - entryArea / aStr.Value, 2.0);
                double dKc_dA = 2.0 * (1.0 - entryArea / aStr.Value) * (-1.0 / aStr.Value);
                dhExp_dA = (Kc * 2 * V * dV_dA + V * V * dKc_dA) / (2 * Hydraulics.G);
            }

            return dhf_dA + dhExp_dA + dhEmp_dA;
        }

        /// <summary>
        /// 计算总能量损失对流量的偏导数 ∂h_L/∂Q（用于雅可比矩阵）。
        /// </summary>
        /// <param name="entryArea">进口面积（m²）。</param>
        /// <param name="flow">流量（m³/s）。</param>
        /// <param name="roughness">曼宁糙率。</param>
        /// <param name="hydraulicRadius">水力半径（m）。</param>
        /// <param name="aStr">库内水面面积（m²，可选）。</param>
        /// <returns>∂h_L/∂Q 的值。</returns>
        public double Dhl_DQ(double entryArea, double flow, double roughness, double hydraulicRadius, double? aStr = null)
        {
            if (!CaptureLosses) return 0.0;

            // 摩阻损失对 Q 的偏导：∂hf/∂Q = ∂Sf/∂Q · L
            double K = Hydraulics.Conveyance(entryArea, roughness, hydraulicRadius);
            double dhf_dQ = Hydraulics.DFrictionSlope_DQ(flow, K) * (ReservoirLength ?? 0);

            // 经验损失对 Q 的偏导
            double V = flow / entryArea;
            double dV_dQ = 1.0 / entryArea;    // dV/dQ = 1/A
            double dhEmp_dQ = KQ * 2 * V * dV_dQ / (2 * Hydraulics.G);

            // 扩散损失对 Q 的偏导
            double dhExp_dQ = 0;
            if (aStr.HasValue)
            {
                double Kc = Math.Pow(1.0 - entryArea / aStr.Value, 2.0);
                dhExp_dQ = Kc * 2 * V * dV_dQ / (2 * Hydraulics.G);
            }

            return dhf_dQ + dhExp_dQ + dhEmp_dQ;
        }

        /// <summary>
        /// 设置水库的面积-水位关系曲线，并可选地更新水位搜索范围。
        /// </summary>
        /// <param name="table">面积曲线数组 [n, 2]（列 0 = 水位 m，列 1 = 面积 m²）。</param>
        /// <param name="alpha">面积缩放系数（默认 1，即不缩放）。</param>
        /// <param name="beta">水位偏移量（默认 0）。</param>
        /// <param name="updateBoundaries">是否用曲线端点更新 _yMin/_yMax（默认 true）。</param>
        public void SetAreaCurve(double[,] table, double alpha = 1, double beta = 0, bool updateBoundaries = true)
        {
            AreaCurve = table;
            _alpha = alpha; _beta = beta;
            int n = table.GetLength(0);
            _areaCurveStages = new double[n];
            _areaCurveAreas  = new double[n];
            for (int i = 0; i < n; i++)
            {
                _areaCurveStages[i] = table[i, 0];   // 水位列
                _areaCurveAreas[i]  = table[i, 1];   // 面积列
            }
            // 用曲线范围更新 Brent 搜索边界
            if (updateBoundaries) { _yMin = _areaCurveStages[0]; _yMax = _areaCurveStages[n - 1]; }
        }

        /// <summary>
        /// 查询给定水位处的水库水面面积（m²）。
        /// 若无面积曲线则返回 SurfaceArea 常数；
        /// 否则对曲线进行线性插值，并应用缩放系数 α。
        /// </summary>
        /// <param name="stage">水位（m）。</param>
        /// <returns>水面面积（m²）。</returns>
        public double AreaAt(double stage)
        {
            if (AreaCurve == null) return SurfaceArea ?? 0;
            // _beta 为水位偏移，线性插值后乘以缩放系数 _alpha
            return _alpha * Hydraulics.Interp(stage + _beta, _areaCurveStages!, _areaCurveAreas!);
        }

        /// <summary>
        /// 计算水位从 y1 变化到 y2 时的净蓄水量变化（m³）。
        /// 若无面积曲线，则等效为矩形库：ΔV = (y2 - y1) · SurfaceArea；
        /// 否则对面积-水位曲线积分（梯形法）：ΔV = ∫_{y1}^{y2} A(y) dy。
        /// </summary>
        /// <param name="y1">起始水位（m）。</param>
        /// <param name="y2">终止水位（m）。</param>
        /// <returns>净蓄水量变化（m³，y2 > y1 时为正值）。</returns>
        public double NetVolChange(double y1, double y2)
        {
            if (AreaCurve == null) return (y2 - y1) * (SurfaceArea ?? 0);

            // 根据水位变化幅度自适应分段（至少 3 段，精度约 0.01 m）
            int n = Math.Max(3, (int)(Math.Abs(y2 - y1) / 0.01) + 2);
            // 生成均匀水位序列
            double[] ys = Enumerable.Range(0, n).Select(i => y1 + (y2 - y1) * i / (n - 1)).ToArray();
            // 各水位处的面积值
            double[] areas = ys.Select(AreaAt).ToArray();
            // 梯形积分 ΔV = ∫ A(y) dy
            return Hydraulics.Trapezoid(areas, ys);
        }

        /// <summary>
        /// 计算给定水位处水库水面面积对水位的导数 dA/dY。
        /// 若无面积曲线则返回 0；否则对面积梯度曲线进行线性插值。
        /// 与 Python <c>lumped_storage.dA_dY(stage)</c> 对应。
        /// </summary>
        /// <param name="stage">水位（m）。</param>
        /// <returns>dA/dY（m²/m = m）。</returns>
        public double DAdy(double stage)
        {
            if (AreaCurve == null || _areaCurveStages == null || _areaCurveAreas == null)
                return 0.0;
            // 与 Python np.gradient 对应：对面积曲线进行数值梯度计算后插值
            double[] grad = Hydraulics.Gradient(_areaCurveAreas, _areaCurveStages);
            return _alpha * Hydraulics.Interp(stage, _areaCurveStages, grad);
        }
    }
}
