using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace FlowSim.Models
{
    /// <summary>
    /// Roseires 水库（苏丹蓝尼罗河）闸控水位-流量关系曲线。
    /// <para>
    /// 功能说明：
    /// <list type="bullet">
    ///   <item>读取实测闸门过闸流量数据，拟合二阶多项式回归模型（spillway / deep sluice）；</item>
    ///   <item>平滑（smooth）模式：按水位偏离初始水位的程度，在"关闸状态"与"开闸状态"之间做三次 S 型插值；</item>
    ///   <item>提供 <see cref="Discharge"/> 和 <see cref="DQ_Dz"/> 接口，与 <see cref="Boundary"/> 直接集成。</item>
    /// </list>
    /// </para>
    /// </summary>
    public class RoseiresRatingCurve : RatingCurve
    {
        // ── 常量 ──
        private const double HydropowerQ      = 63.0e6 / (24 * 3600);  // ≈ 729.2 m³/s
        private const int    NumSluiceGates   = 5;
        private const int    NumSpillways     = 7;
        private const double MaxSpillwayOpen  = 13.0;                    // m
        private const double MinStage         = 466.7;                   // m
        private const double MaxStage         = 492.0;                   // m
        private const double TailWaterAvg     = 447.5;                   // (440+455)/2

        // ── 模型参数 ──
        private readonly double _initialStage;
        private readonly double _buffer;
        private readonly int    _jammedSpillways;
        private readonly int    _jammedSluiceGates;
        private readonly int    _activeSpillways;
        private readonly int    _activeSluices;

        // ── 回归模型系数（intercept + 5 项二阶多项式：x1,x2,x1²,x1x2,x2²）──
        private double[]? _spillwayCoeffs;   // predict(stage, opening) → Q per spillway
        private double[]? _sluiceCoeffs;     // predict(stage, twl)    → Q per sluice gate

        // ── 关闸（低释放）与开闸（高释放）状态缓存 ──
        /// <summary>关闸时各溢洪道开度（m）列表。</summary>
        private double[]? _closedSpillwayOpenings;
        /// <summary>关闸时开启的深孔泄槽数量。</summary>
        private int _closedSluicesNum;
        /// <summary>开闸时各溢洪道开度（全开非卡闸孔，其余 0）。</summary>
        private double[]? _openSpillwayOpenings;
        /// <summary>开闸时开启的深孔泄槽数量（全部非卡闸孔）。</summary>
        private int _openSluicesNum;

        // ====================================================================
        // 构造函数
        // ====================================================================

        /// <summary>
        /// 构建 Roseires 水库水位-流量关系。
        /// </summary>
        /// <param name="initialStage">初始水位（m），应在 466.7–492 m 之间。</param>
        /// <param name="initialFlow">初始流量（m³/s），用于计算关闸状态（低释放状态）下所需最小闸门开度。</param>
        /// <param name="spillwayDataPath">溢洪道流量数据 CSV 路径（行=水位，列=开度）。</param>
        /// <param name="sluiceDataPath">深孔泄槽流量数据 CSV 路径（行=水位，列=尾水位）。</param>
        /// <param name="jammedSpillways">被卡住（无法操作）的溢洪道数量（默认 0）。</param>
        /// <param name="jammedSluiceGates">被卡住的深孔泄槽数量（默认 0）。</param>
        /// <param name="buffer">平滑插值的水位缓冲区（m，默认 0.5）。</param>
        public RoseiresRatingCurve(
            double initialStage,
            double initialFlow,
            string spillwayDataPath,
            string sluiceDataPath,
            int    jammedSpillways  = 0,
            int    jammedSluiceGates = 0,
            double buffer = 0.5)
        {
            if (initialStage < MinStage || initialStage > MaxStage)
                throw new ArgumentOutOfRangeException(nameof(initialStage),
                    $"Roseires 水位须在 {MinStage}–{MaxStage} m 之间，当前 {initialStage} m。");

            _initialStage      = initialStage;
            _buffer            = buffer;
            _jammedSpillways   = Math.Min(jammedSpillways,   NumSpillways);
            _jammedSluiceGates = Math.Min(jammedSluiceGates, NumSluiceGates);
            _activeSpillways   = NumSpillways   - _jammedSpillways;
            _activeSluices     = NumSluiceGates - _jammedSluiceGates;

            // 拟合两套多项式回归模型
            FitModels(spillwayDataPath, sluiceDataPath);

            // 设置开闸（全开）状态
            _openSpillwayOpenings = BuildOpenings(_activeSpillways, MaxSpillwayOpen);
            _openSluicesNum       = _activeSluices;

            // 计算关闸（低释放）状态
            CalcClosedState(initialStage, initialFlow);

            // 将此对象标记为"已定义"，使基类 Discharge/DQ_Dz 不会因 Defined=false 而报错
            Set(RatingCurveType.Power, 1, 1);  // 占位，实际 Discharge 已被覆盖
        }

        // ====================================================================
        // 覆盖基类方法
        // ====================================================================

        /// <summary>
        /// 以平滑插值方式计算 Roseires 闸门出流量 Q（m³/s）。
        /// 当水位恰好等于初始水位时，返回关闸低释放流量；
        /// 高于初始水位 <see cref="_buffer"/> m 时，全切换至开闸高释放；
        /// 中间区域以三次 S 型曲线平滑过渡。
        /// </summary>
        public override double Discharge(double stage, double time = 0)
        {
            double alpha = AlphaSmooth(stage);
            double lowQ  = TotalRelease(stage, _closedSpillwayOpenings!, _closedSluicesNum);
            double highQ = TotalRelease(stage, _openSpillwayOpenings!,   _openSluicesNum);
            return (1.0 - alpha) * lowQ + alpha * highQ;
        }

        /// <summary>
        /// dQ/dZ（数值微分，步长 Δz=0.001 m）。
        /// </summary>
        public override double DQ_Dz(double stage, double time = 0)
        {
            const double dY = 0.001;
            return (Discharge(stage + dY, time) - Discharge(stage - dY, time)) / (2 * dY);
        }

        // ====================================================================
        // 状态初始化
        // ====================================================================

        /// <summary>
        /// 计算关闸（低释放）状态：在初始水位下，刚好满足 initialFlow 的最小闸门配置。
        /// 策略：先确定需要开启的深孔泄槽数，再确定需要的溢洪道部分开度。
        /// </summary>
        private void CalcClosedState(double stage, double initialFlow)
        {
            // ① 先令全部非卡闸溢洪道全开，逐步增加深孔泄槽直到 Q > initialFlow
            int sluicesToOpen = 0;
            for (int i = 1; i <= _activeSluices; i++)
            {
                double q = TotalRelease(stage,
                    BuildOpenings(_activeSpillways, MaxSpillwayOpen), i);
                if (q > initialFlow) { sluicesToOpen = i - 1; break; }
                sluicesToOpen = i;
            }

            // ② 逐步增加全开溢洪道数，直到 Q > initialFlow
            int fullyOpen = 0;
            for (int i = 1; i <= _activeSpillways; i++)
            {
                double q = TotalRelease(stage,
                    BuildOpeningsPartial(i, MaxSpillwayOpen, 0, _activeSpillways), sluicesToOpen);
                if (q > initialFlow) { fullyOpen = i - 1; break; }
                fullyOpen = i;
            }

            // ③ 二分（Brentq）确定第 (fullyOpen+1) 扇溢洪道的部分开度
            double partialOpen = 0;
            if (fullyOpen < _activeSpillways)
            {
                try
                {
                    partialOpen = GerdHydrograph.Brentq(x =>
                        initialFlow - TotalRelease(stage,
                            BuildOpeningsPartial(fullyOpen, MaxSpillwayOpen, x, _activeSpillways),
                            sluicesToOpen),
                        0, MaxSpillwayOpen);
                    partialOpen = Math.Round(partialOpen, 2);
                }
                catch { partialOpen = 0; }
            }

            _closedSpillwayOpenings = BuildOpeningsPartial(fullyOpen, MaxSpillwayOpen, partialOpen, _activeSpillways);
            _closedSluicesNum       = sluicesToOpen;
        }

        // ====================================================================
        // 出流计算（内部）
        // ====================================================================

        private double TotalRelease(double stage, double[] spillwayOpenings, int numOpenSluices)
        {
            double spillwayQ = 0;
            for (int j = 0; j < spillwayOpenings.Length; j++)
                if (spillwayOpenings[j] > 0)
                    spillwayQ += SpillwayQ(stage, spillwayOpenings[j]);

            double sluiceQ = SluiceQ(stage, TailWaterAvg) * numOpenSluices;
            return spillwayQ + sluiceQ + HydropowerQ;
        }

        private double SpillwayQ(double stage, double opening) =>
            PolyPredict(_spillwayCoeffs!, stage, opening);

        private double SluiceQ(double stage, double twl) =>
            PolyPredict(_sluiceCoeffs!, stage, twl);

        // ── 平滑插值 ──
        private double AlphaSmooth(double stage)
        {
            if (stage >= _initialStage + _buffer) return 1.0;
            if (stage <= _initialStage)           return 0.0;
            double s = (stage - _initialStage) / _buffer;
            return 3 * s * s - 2 * s * s * s;  // 三次 Hermite 平滑
        }

        // ====================================================================
        // 多项式拟合（degree=2，2 特征，含截距）
        // ====================================================================

        private void FitModels(string spillwayPath, string sluicePath)
        {
            _spillwayCoeffs = FitPoly2(spillwayPath);
            _sluiceCoeffs   = FitPoly2(sluicePath);
        }

        /// <summary>
        /// 从 CSV 矩阵（行=stage，列=第二特征）读取数据并拟合二阶多项式 OLS 模型。
        /// CSV 格式：第一行=列头（第二特征值）, 第一列=行头（水位值），其余格=流量。
        /// 系数顺序：[截距, x1, x2, x1², x1·x2, x2²]
        /// </summary>
        private static double[] FitPoly2(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2)
                throw new InvalidOperationException($"数据文件 '{path}' 行数不足。");

            // 读取列头（第二特征值：开度或尾水位）
            string headerLine = lines[0].TrimStart('\uFEFF');
            var headerParts = headerLine.Split(',');
            var x2Vals = new List<double>();
            for (int j = 1; j < headerParts.Length; j++)
                if (double.TryParse(headerParts[j].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v2))
                    x2Vals.Add(v2);

            // 读取每行（水位 + 流量矩阵）
            var X1 = new List<double>();
            var X2 = new List<double>();
            var Y  = new List<double>();

            for (int i = 1; i < lines.Length; i++)
            {
                var parts = lines[i].Split(',');
                if (parts.Length < 2) continue;
                if (!double.TryParse(parts[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double stage))
                    continue;

                for (int j = 0; j < x2Vals.Count; j++)
                {
                    if (j + 1 >= parts.Length) break;
                    if (!double.TryParse(parts[j + 1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double discharge))
                        continue;
                    X1.Add(stage);
                    X2.Add(x2Vals[j]);
                    Y.Add(discharge);
                }
            }

            int n = X1.Count;
            if (n < 6)
                throw new InvalidOperationException($"数据点不足（需要 ≥6 点，实际 {n} 点）：{path}");

            // 构造设计矩阵 [1, x1, x2, x1², x1·x2, x2²]
            var Xmat = DenseMatrix.Create(n, 6, (i, j) =>
            {
                double x1 = X1[i], x2 = X2[i];
                return j switch
                {
                    0 => 1.0,
                    1 => x1,
                    2 => x2,
                    3 => x1 * x1,
                    4 => x1 * x2,
                    5 => x2 * x2,
                    _ => 0.0
                };
            });

            var yVec = DenseVector.OfArray(Y.ToArray());

            // OLS: β = (XᵀX)⁻¹ · Xᵀy  → 用 QR 分解最小二乘
            var beta = Xmat.Solve(yVec);  // MathNet 最小范数解
            return beta.ToArray();
        }

        private static double PolyPredict(double[] coeffs, double x1, double x2)
            => coeffs[0]
             + coeffs[1] * x1
             + coeffs[2] * x2
             + coeffs[3] * x1 * x1
             + coeffs[4] * x1 * x2
             + coeffs[5] * x2 * x2;

        // ====================================================================
        // 辅助：构造溢洪道开度数组
        // ====================================================================

        private static double[] BuildOpenings(int active, double opening)
        {
            var arr = new double[NumSpillways];
            for (int i = 0; i < active; i++) arr[i] = opening;
            return arr;
        }

        private static double[] BuildOpeningsPartial(int fullyOpen, double fullOpen,
                                                      double partialOpen, int active)
        {
            var arr = new double[NumSpillways];
            for (int i = 0; i < Math.Min(fullyOpen, active); i++) arr[i] = fullOpen;
            if (fullyOpen < active && partialOpen > 0)
                arr[fullyOpen] = partialOpen;
            return arr;
        }
    }
}
