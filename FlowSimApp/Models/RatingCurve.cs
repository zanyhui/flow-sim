using System;
using System.Linq;

namespace FlowSim.Models
{
    /// <summary>
    /// 水位-流量关系曲线类型枚举。
    /// <list type="bullet">
    ///   <item><term>Polynomial</term><description>二次多项式：Q = A·Z² + B·Z + C。</description></item>
    ///   <item><term>Power</term><description>幂律函数：Q = A·Z^B（对数线性，适合大多数天然河道）。</description></item>
    /// </list>
    /// </summary>
    public enum RatingCurveType { Polynomial, Power }

    /// <summary>
    /// 水位-流量关系曲线（Rating Curve）类。
    /// <para>
    /// 功能：
    /// <list type="bullet">
    ///   <item>通过 <see cref="Set"/> 方法直接指定参数（A、B、C）；</item>
    ///   <item>通过 <see cref="Fit"/> 方法从实测数据拟合（最小二乘法）；</item>
    ///   <item>提供 <see cref="Discharge"/> 由水位查流量；</item>
    ///   <item>提供 <see cref="DQ_Dz"/> 流量对水位的导数（用于雅可比矩阵）；</item>
    ///   <item>提供 <see cref="Stage"/> 由流量反查水位（牛顿迭代法）。</item>
    /// </list>
    /// </para>
    /// </summary>
    public class RatingCurve
    {
        /// <summary>曲线是否已定义（调用 Set 或 Fit 后为 true）。</summary>
        public bool Defined { get; private set; }

        /// <summary>曲线类型（多项式或幂律）。</summary>
        public RatingCurveType Type { get; private set; }

        /// <summary>多项式系数 A（或幂律系数 A）。</summary>
        public double A { get; private set; }

        /// <summary>多项式系数 B（或幂律指数 B）。</summary>
        public double B { get; private set; }

        /// <summary>多项式系数 C（仅多项式类型使用）。</summary>
        public double C { get; private set; }

        /// <summary>水位偏移量（计算时对水位加此偏移后再代入公式）。</summary>
        public double StageShift { get; private set; }

        // 可选：用户提供的自定义函数替代参数化公式
        private Func<double, double>? _function;    // 查流量函数
        private Func<double, double>? _derivative;  // 流量对水位的导数函数

        /// <summary>
        /// 直接设定曲线参数。
        /// </summary>
        /// <param name="type">曲线类型（多项式 or 幂律）。</param>
        /// <param name="a">系数 A（幂律时为比例系数）。</param>
        /// <param name="b">系数 B（幂律时为指数）。</param>
        /// <param name="c">系数 C（仅多项式使用，默认 0）。</param>
        /// <param name="stageShift">水位偏移（默认 0）。</param>
        public void Set(RatingCurveType type, double a, double b, double c = 0, double stageShift = 0)
        {
            Type = type;
            A = a; B = b; C = c;
            StageShift = stageShift;
            _function = null; _derivative = null;   // 清除自定义函数
            Defined = true;
        }

        /// <summary>
        /// 由给定水位计算流量 Q。
        /// <para>
        /// 多项式：Q = A·(Z+shift)² + B·(Z+shift) + C；
        /// 幂律：Q = A·(Z+shift)^B。
        /// </para>
        /// </summary>
        /// <param name="stage">水位（m）。</param>
        /// <param name="time">时间（秒），预留给时变曲线（当前未使用）。</param>
        /// <returns>对应流量 Q（m³/s）。</returns>
        public virtual double Discharge(double stage, double time = 0)
        {
            if (!Defined) throw new InvalidOperationException("Rating curve is undefined.");
            if (_function != null) return _function(stage);   // 优先使用自定义函数
            double x = stage + StageShift;   // 应用水位偏移
            return Type == RatingCurveType.Polynomial
                ? A * x * x + B * x + C       // 二次多项式
                : A * Math.Pow(x, B);          // 幂律函数
        }

        /// <summary>
        /// 计算流量对水位的导数 dQ/dZ（用于雅可比矩阵中的边界项）。
        /// <para>
        /// 多项式：dQ/dZ = 2A·(Z+shift) + B；
        /// 幂律：dQ/dZ = A·B·(Z+shift)^(B-1)。
        /// </para>
        /// </summary>
        /// <param name="stage">水位（m）。</param>
        /// <param name="time">时间（秒，预留）。</param>
        /// <returns>dQ/dZ 的值。</returns>
        public virtual double DQ_Dz(double stage, double time = 0)
        {
            if (!Defined) throw new InvalidOperationException("Rating curve is undefined.");
            if (_derivative != null) return _derivative(stage);
            double Y = stage + StageShift;
            return Type == RatingCurveType.Polynomial
                ? A * 2.0 * Y + B              // 多项式一阶导
                : A * B * Math.Pow(Y, B - 1.0); // 幂律一阶导
        }

        /// <summary>
        /// 由流量反查水位（牛顿迭代法）。
        /// 迭代公式：Z_{n+1} = Z_n - (Q(Z_n) - target) / (dQ/dZ)|_{Z_n}
        /// 最多迭代 100 次，收敛条件为 |Q(Z) - target| &lt; tol。
        /// </summary>
        /// <param name="discharge">目标流量（m³/s）。</param>
        /// <param name="trialStage">初始猜测水位（m，默认由 StageShift 决定）。</param>
        /// <param name="tol">收敛容差（m³/s，默认 0.01）。</param>
        /// <returns>对应水位（m）。</returns>
        public double Stage(double discharge, double trialStage = double.NaN, double tol = 1e-2)
        {
            if (!Defined) throw new InvalidOperationException("Rating curve is undefined.");
            // 初始猜测：取偏移量附近稍高一点，避免幂律在 Z=0 处无效
            if (double.IsNaN(trialStage)) trialStage = -StageShift * 1.05;
            double q = Discharge(trialStage);

            for (int i = 0; i < 100 && Math.Abs(q - discharge) > tol; i++)
            {
                double d = DQ_Dz(trialStage);
                if (Math.Abs(d) < 1e-12) break;              // 导数近零时停止（避免除零）
                trialStage -= (q - discharge) / d;            // 牛顿步
                q = Discharge(trialStage);
            }
            return trialStage;
        }

        /// <summary>
        /// 从实测流量-水位数据拟合水位流量关系曲线。
        /// <para>
        /// 多项式拟合（最小二乘法）：构造 3×3 正规方程 [M]{A,B,C}={T}，
        /// 用部分主元高斯消元法 (<see cref="SolveLinear3x3"/>) 求解系数。
        /// 幂律拟合：对流量和偏移水位取对数后做线性回归（log Q = log A + B·log Z），
        /// 由斜率和截距反算 A、B。
        /// </para>
        /// </summary>
        /// <param name="discharges">实测流量数组（m³/s），至少 3 个点。</param>
        /// <param name="stages">对应水位数组（m）。</param>
        /// <param name="type">曲线类型（多项式 or 幂律）。</param>
        /// <param name="stageShift">水位偏移（默认 0；幂律时常取负的最低水位）。</param>
        public void Fit(double[] discharges, double[] stages, RatingCurveType type = RatingCurveType.Polynomial, double stageShift = 0)
        {
            if (discharges.Length < 3) throw new ArgumentException("Need at least 3 points.");
            Type = type;
            StageShift = stageShift;
            double[] shifted = stages.Select(s => s + stageShift).ToArray();  // 应用偏移

            if (type == RatingCurveType.Polynomial)
            {
                // 最小二乘拟合二次多项式：Q = A·Z² + B·Z + C
                // 构造正规方程 Σ(Z^n * Q) = Σ(Z^(n+m) * coeff_m)
                double s0 = shifted.Length;
                double s1 = shifted.Sum();
                double s2 = shifted.Sum(x => x * x);
                double s3 = shifted.Sum(x => x * x * x);
                double s4 = shifted.Sum(x => x * x * x * x);
                double t0 = discharges.Sum();
                double t1 = 0, t2 = 0;
                for (int i = 0; i < shifted.Length; i++)
                {
                    t1 += discharges[i] * shifted[i];          // Σ(Q * Z)
                    t2 += discharges[i] * shifted[i] * shifted[i]; // Σ(Q * Z²)
                }
                // 正规方程系数矩阵（对称正定）
                double[,] M = { { s4, s3, s2 }, { s3, s2, s1 }, { s2, s1, s0 } };
                double[] rhs = { t2, t1, t0 };
                double[] sol = SolveLinear3x3(M, rhs);
                A = sol[0]; B = sol[1]; C = sol[2];
            }
            else
            {
                // 幂律拟合：log(Q) = log(A) + B·log(Z)，即对数空间的线性回归
                double[] logY = shifted.Select(y => Math.Log(y)).ToArray();
                double[] logQ = discharges.Select(q => Math.Log(q)).ToArray();
                double meanX = logY.Average(), meanY = logQ.Average();
                double num = 0, den = 0;
                for (int i = 0; i < logY.Length; i++)
                {
                    num += (logY[i] - meanX) * (logQ[i] - meanY);  // 协方差
                    den += (logY[i] - meanX) * (logY[i] - meanX);  // 方差
                }
                B = den > 0 ? num / den : 1;           // 斜率 = 指数 B
                A = Math.Exp(meanY - B * meanX);        // 截距反算比例系数 A
            }
            Defined = true;
        }

        /// <summary>
        /// 用部分主元高斯消元法求解 3×3 线性方程组 M·x = rhs。
        /// <para>
        /// 算法步骤：
        /// 1. 前向消元：每列选取绝对值最大的行（主元行），交换行后消去下方元素；
        /// 2. 回代：从最后一行向前逐一求解各未知数。
        /// </para>
        /// </summary>
        /// <param name="M">3×3 系数矩阵。</param>
        /// <param name="rhs">右端项向量（长度 3）。</param>
        /// <returns>解向量 x（长度 3）。</returns>
        private static double[] SolveLinear3x3(double[,] M, double[] rhs)
        {
            double[,] a = (double[,])M.Clone();    // 复制避免修改原矩阵
            double[] b = (double[])rhs.Clone();
            const double epsilon = 1e-12;           // 奇异性判断阈值
            int n = 3;

            // 前向消元（部分主元法）
            for (int k = 0; k < n; k++)
            {
                // 寻找当前列绝对值最大的行（主元行），提高数值稳定性
                int pivot = k;
                for (int i = k + 1; i < n; i++)
                    if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k])) pivot = i;

                // 行交换：将主元行移至对角线位置
                for (int j = 0; j < n; j++) { double t = a[k, j]; a[k, j] = a[pivot, j]; a[pivot, j] = t; }
                { double t = b[k]; b[k] = b[pivot]; b[pivot] = t; }

                if (Math.Abs(a[k, k]) < epsilon)
                    throw new InvalidOperationException("Singular matrix in rating curve polynomial fit.");

                // 消去主元列下方的所有元素
                for (int i = k + 1; i < n; i++)
                {
                    double f = a[i, k] / a[k, k];   // 消元因子
                    for (int j = k; j < n; j++) a[i, j] -= f * a[k, j];
                    b[i] -= f * b[k];
                }
            }

            // 回代求解
            double[] x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                x[i] = b[i];
                for (int j = i + 1; j < n; j++) x[i] -= a[i, j] * x[j];
                if (Math.Abs(a[i, i]) < epsilon)
                    throw new InvalidOperationException("Singular matrix in rating curve polynomial fit.");
                x[i] /= a[i, i];   // 除以对角元素得到 x_i
            }
            return x;
        }
    }
}
