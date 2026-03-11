using System;

namespace FlowSim.Models
{
    /// <summary>
    /// 水力学静态工具类，封装基于曼宁公式和圣维南方程组的各类计算函数。
    /// <para>
    /// 主要包含：
    /// <list type="bullet">
    ///   <item>输水能力 K（Manning）及其关于过水面积 A 的导数</item>
    ///   <item>摩阻坡度 Sf 及其对 A、Q 的偏导数</item>
    ///   <item>正常流量 Qn 及其导数</item>
    ///   <item>弗劳德数 Fr 及其偏导数</item>
    ///   <item>弯曲坡度 Sc（适用于弯道修正）及其偏导数</item>
    ///   <item>Brent 方法求根、线性插值、数值梯度、梯形积分等数学工具</item>
    /// </list>
    /// </para>
    /// </summary>
    public static class Hydraulics
    {
        /// <summary>重力加速度 g = 9.80665 m/s²（标准值）。</summary>
        public const double G = 9.80665;

        /// <summary>
        /// 干断面输水能力阈值：输水能力 K 小于此值时视为干断面（无水流），
        /// 摩阻坡度计算返回 0 以防止除零产生 NaN/Inf。
        /// </summary>
        private const double DrySectionThreshold = 1e-10;

        /// <summary>
        /// 计算曼宁输水能力 K = A * R^(2/3) / n。
        /// <para>
        /// 曼宁公式：Q = K * sqrt(S0)，其中 K 综合了断面面积与糙率的影响。
        /// </para>
        /// </summary>
        /// <param name="A">过水面积（m²）。</param>
        /// <param name="n">曼宁糙率系数（无量纲）。</param>
        /// <param name="R">水力半径 R = A/P（m）。</param>
        /// <returns>输水能力 K（m³/s）。</returns>
        public static double Conveyance(double A, double n, double R)
        {
            return A * Math.Pow(R, 2.0 / 3.0) / n;
        }

        /// <summary>
        /// 计算输水能力对过水面积的偏导数 dK/dA。
        /// <para>
        /// 由链式法则展开：dK/dA = (R^(2/3) + A*(2/3)*R^(-1/3)*dR/dA) / n。
        /// </para>
        /// </summary>
        /// <param name="A">过水面积（m²）。</param>
        /// <param name="n">曼宁糙率系数。</param>
        /// <param name="R">水力半径（m）。</param>
        /// <param name="dR_dA">水力半径对面积的导数 dR/dA。</param>
        /// <returns>dK/dA 的值。</returns>
        public static double DConveyance_DA(double A, double n, double R, double dR_dA)
        {
            return (Math.Pow(R, 2.0 / 3.0) + A * (2.0 / 3.0) * Math.Pow(R, 2.0 / 3.0 - 1.0) * dR_dA) / n;
        }

        /// <summary>
        /// 计算摩阻坡度 Sf = Q*|Q| / K²。
        /// <para>
        /// Q*|Q| 的写法可以保留流向符号（负流量时 Sf 为负，阻力与流向相反）。
        /// </para>
        /// </summary>
        /// <param name="Q">流量（m³/s）。</param>
        /// <param name="K">输水能力（m³/s）。</param>
        /// <returns>摩阻坡度 Sf（无量纲）。</returns>
        public static double FrictionSlope(double Q, double K)
        {
            if (K < DrySectionThreshold) return 0;   // 干断面（K≈0）时无摩阻；防止除零产生 NaN/Inf
            return Q * Math.Abs(Q) / (K * K);
        }

        /// <summary>
        /// 计算摩阻坡度对过水面积的偏导数 dSf/dA。
        /// <para>
        /// 由链式法则：dSf/dA = -2 * Sf * (dK/dA) / K。
        /// </para>
        /// </summary>
        /// <param name="Q">流量（m³/s）。</param>
        /// <param name="K">输水能力（m³/s）。</param>
        /// <param name="dK_dA">dK/dA 的值。</param>
        /// <returns>dSf/dA 的值。</returns>
        public static double DFrictionSlope_DA(double Q, double K, double dK_dA)
        {
            if (K < DrySectionThreshold) return 0;   // 干断面时返回 0，与 FrictionSlope 保持一致
            return -2.0 * FrictionSlope(Q, K) * (dK_dA / K);
        }

        /// <summary>
        /// 计算摩阻坡度对流量的偏导数 dSf/dQ = 2*|Q| / K²。
        /// </summary>
        /// <param name="Q">流量（m³/s）。</param>
        /// <param name="K">输水能力（m³/s）。</param>
        /// <returns>dSf/dQ 的值。</returns>
        public static double DFrictionSlope_DQ(double Q, double K)
        {
            if (K < DrySectionThreshold) return 0;   // 干断面时返回 0
            return 2.0 * Math.Abs(Q) / (K * K);
        }

        /// <summary>
        /// 计算正常流量 Qn = K * sqrt(|S0|)。
        /// 若 S0 为负（逆坡），则流量取负值（水向上游流）。
        /// </summary>
        /// <param name="bedSlope">河床纵坡 S0（无量纲，下坡为正）。</param>
        /// <param name="K">输水能力（m³/s）。</param>
        /// <returns>正常流量 Qn（m³/s）。</returns>
        public static double NormalFlow(double bedSlope, double K)
        {
            double Q = K * Math.Sqrt(Math.Abs(bedSlope));
            if (bedSlope < 0) Q = -Q;  // 逆坡时流量取负
            return Q;
        }

        /// <summary>
        /// 计算正常流量对过水面积的偏导数 dQn/dA。
        /// 推导：Qn = K(A) * sqrt(|S0|)，故 dQn/dA = dK/dA * sqrt(|S0|)。
        /// </summary>
        /// <param name="S0">河床纵坡（无量纲）。</param>
        /// <param name="dK_dA">dK/dA 的值。</param>
        /// <returns>dQn/dA 的值。</returns>
        public static double DNormalFlow_DA(double S0, double dK_dA)
        {
            double dQ = dK_dA * Math.Sqrt(Math.Abs(S0));
            if (S0 < 0) dQ = -dQ;
            return dQ;
        }

        /// <summary>
        /// 计算弗劳德数 Fr = V / sqrt(g * D)，其中 D = A/T 为水力深度。
        /// 对极小的 A、T、D 设置下限 1e-6 以避免除零。
        /// </summary>
        /// <param name="T">水面宽度（m）。</param>
        /// <param name="A">过水面积（m²）。</param>
        /// <param name="Q">流量（m³/s）。</param>
        /// <returns>弗劳德数 Fr（无量纲；>1 为超临界流）。</returns>
        public static double FroudeNumber(double T, double A, double Q)
        {
            double V = Q / Math.Max(A, 1e-6);                     // 断面平均流速（m/s）
            double D = A / Math.Max(T, 1e-6);                     // 水力深度（m）
            return V / Math.Sqrt(G * Math.Max(D, 1e-6));          // Fr = V / sqrt(gD)
        }

        /// <summary>
        /// 计算弗劳德数对过水面积的偏导数 dFr/dA。
        /// 使用商法则和链式法则展开。
        /// </summary>
        /// <param name="T">水面宽度（m）。</param>
        /// <param name="A">过水面积（m²）。</param>
        /// <param name="Q">流量（m³/s）。</param>
        /// <returns>dFr/dA 的值。</returns>
        public static double DFroude_DA(double T, double A, double Q)
        {
            double V = Q / A;           // 流速
            double D = A / T;           // 水力深度
            double dV_dA = -Q / (A * A);              // dV/dA = -Q/A²
            double dD_dA = 1.0 / T;                    // dD/dA = 1/T
            // Fr = V / sqrt(gD)，由商法则展开 dFr/dA
            return -0.5 * V * Math.Pow(G * D, -1.5) * G * dD_dA + dV_dA * Math.Pow(G * D, -0.5);
        }

        /// <summary>
        /// 计算弗劳德数对流量的偏导数 dFr/dQ。
        /// Fr = V/sqrt(gD)，V = Q/A，故 dFr/dQ = (1/A) / sqrt(gD)。
        /// </summary>
        /// <param name="T">水面宽度（m）。</param>
        /// <param name="A">过水面积（m²）。</param>
        /// <returns>dFr/dQ 的值。</returns>
        public static double DFroude_DQ(double T, double A)
        {
            double D = A / T;           // 水力深度
            double dV_dQ = 1.0 / A;    // dV/dQ = 1/A
            return dV_dQ * Math.Pow(G * D, -0.5);
        }

        /// <summary>
        /// 计算 Darcy-Weisbach 摩阻系数 f = 8g / C²，
        /// 其中谢才系数 C = R^(1/6) / n（Manning-Chézy 关系）。
        /// </summary>
        /// <param name="n">曼宁糙率系数。</param>
        /// <param name="R">水力半径（m）。</param>
        /// <returns>Darcy-Weisbach 阻力系数 f（无量纲）。</returns>
        public static double DarcyWeisbachF(double n, double R)
        {
            double C = Math.Pow(R, 1.0 / 6.0) / n;    // 谢才系数 C（m^0.5/s）
            return 8.0 * G / (C * C);
        }

        /// <summary>
        /// 计算弯曲坡度 Sc（弯道二次流对水位梯度的修正项）。
        /// <para>
        /// 公式来源于经验公式（Rozovskii/Kikkawa 等）：
        /// Sc = [(2.86*sqrt(f) + 2.07*f) * h² * Fr²] / [(0.565 + sqrt(f)) * rc²]
        /// 其中 rc = 弯道曲率半径（m），f = Darcy-Weisbach 系数，Fr = 弗劳德数。
        /// </para>
        /// </summary>
        /// <param name="h">水深（m）。</param>
        /// <param name="T">水面宽度（m）。</param>
        /// <param name="A">过水面积（m²）。</param>
        /// <param name="Q">流量（m³/s）。</param>
        /// <param name="n">曼宁糙率系数。</param>
        /// <param name="R">水力半径（m）。</param>
        /// <param name="rc">弯道曲率半径（m）。</param>
        /// <returns>弯曲坡度 Sc（无量纲）。</returns>
        public static double CurvatureSlope(double h, double T, double A, double Q, double n, double R, double rc)
        {
            double Fr = FroudeNumber(T, A, Q);
            double f = DarcyWeisbachF(n, R);
            double numerator = (2.86 * Math.Sqrt(f) + 2.07 * f) * h * h * Fr * Fr;
            double denominator = (0.565 + Math.Sqrt(f)) * rc * rc;
            return numerator / denominator;
        }

        /// <summary>
        /// 计算弯曲坡度对过水面积的偏导数 dSc/dA（用于雅可比矩阵）。
        /// 采用商法则对 Sc 关于 A 求偏导，通过链式法则包含 dFr/dA 和 df/dA。
        /// </summary>
        /// <param name="h">水深（m）。</param>
        /// <param name="A">过水面积（m²）。</param>
        /// <param name="Q">流量（m³/s）。</param>
        /// <param name="n">曼宁糙率系数。</param>
        /// <param name="R">水力半径（m）。</param>
        /// <param name="rc">弯道曲率半径（m）。</param>
        /// <param name="dR_dA">水力半径对面积的导数。</param>
        /// <param name="T">水面宽度（m）。</param>
        /// <returns>dSc/dA 的值。</returns>
        public static double DCurvatureSlope_DA(double h, double A, double Q, double n, double R, double rc, double dR_dA, double T)
        {
            double Fr = FroudeNumber(T, A, Q);
            double C = Math.Pow(R, 1.0 / 6.0) / n;
            double f = 8.0 * G / (C * C);
            double dh_dA = 1.0 / T;                  // dh/dA ≈ 1/T（水面宽）
            double dFr_dA = DFroude_DA(T, A, Q);
            // df/dA = -( 8/3 ) * g * n² * R^(-4/3) * dR/dA
            double df_dA = -(8.0 / 3.0) * G * n * n * Math.Pow(R, -4.0 / 3.0) * dR_dA;
            double sqrtf = Math.Sqrt(f);
            // 分子和分母的商法则展开
            double num = (2.86 * sqrtf + 2.07 * f) * h * h * Fr * Fr;
            double den = (0.565 + sqrtf) * rc * rc;
            double dnum_dA = (2.86 / (2.0 * sqrtf) * df_dA + 2.07 * df_dA) * h * h * Fr * Fr
                           + (2.86 * sqrtf + 2.07 * f) * (2.0 * h * dh_dA * Fr * Fr + h * h * 2.0 * Fr * dFr_dA);
            double dden_dA = (1.0 / (2.0 * sqrtf) * df_dA) * rc * rc;
            return (dnum_dA * den - num * dden_dA) / (den * den);
        }

        /// <summary>
        /// 计算弯曲坡度对流量的偏导数 dSc/dQ（用于雅可比矩阵）。
        /// 因 f 不依赖 Q，仅 Fr 依赖 Q，故偏导相对简单。
        /// </summary>
        /// <param name="h">水深（m）。</param>
        /// <param name="T">水面宽度（m）。</param>
        /// <param name="A">过水面积（m²）。</param>
        /// <param name="Q">流量（m³/s）。</param>
        /// <param name="n">曼宁糙率系数。</param>
        /// <param name="R">水力半径（m）。</param>
        /// <param name="rc">弯道曲率半径（m）。</param>
        /// <returns>dSc/dQ 的值。</returns>
        public static double DCurvatureSlope_DQ(double h, double T, double A, double Q, double n, double R, double rc)
        {
            double Fr = FroudeNumber(T, A, Q);
            double C = Math.Pow(R, 1.0 / 6.0) / n;
            double f = 8.0 * G / (C * C);
            double dFr_dQ = DFroude_DQ(T, A);
            double sqrtf = Math.Sqrt(f);
            double den = (0.565 + sqrtf) * rc * rc;
            // 分子对 Q 的偏导：仅 Fr² 项依赖 Q
            double dnum_dQ = (2.86 * sqrtf + 2.07 * f) * h * h * 2.0 * Fr * dFr_dQ;
            return dnum_dQ / den;
        }

        /// <summary>
        /// Brent 方法求解方程 f(x)=0 在区间 [a,b] 内的根。
        /// <para>
        /// Brent 方法结合了二分法、割线法和逆二次插值法，在保证收敛性的同时具有超线性收敛速度。
        /// 算法流程：
        /// 1. 若满足逆二次插值/割线法条件则使用，否则退化为二分法；
        /// 2. 每步更新区间 [a,b] 使得 f(a)*f(b) 保持异号；
        /// 3. 满足容差 tol 或达到最大迭代数 maxIter 时停止。
        /// </para>
        /// </summary>
        /// <param name="f">目标方程函数。</param>
        /// <param name="a">搜索区间左端点。</param>
        /// <param name="b">搜索区间右端点（要求 f(a)*f(b) &lt;= 0）。</param>
        /// <param name="tol">收敛容差（默认 1e-8）。</param>
        /// <param name="maxIter">最大迭代次数（默认 100）。</param>
        /// <returns>方程的根 x*，满足 |f(x*)| &lt; tol 或 |b-a| &lt; tol。</returns>
        public static double Brentq(Func<double, double> f, double a, double b, double tol = 1e-8, int maxIter = 100)
        {
            double fa = f(a);
            double fb = f(b);
            // 前提条件：端点函数值异号
            if (fa * fb > 0)
                throw new ArgumentException($"f(a) and f(b) must have opposite signs. f({a})={fa}, f({b})={fb}");
            if (Math.Abs(fa) < tol) return a;
            if (Math.Abs(fb) < tol) return b;

            double c = a, fc = fa;   // c 是上一次的 b（用于逆二次插值）
            double s = 0, fs = 0;    // 本次试探点及其函数值
            double d = 0;            // 上上次的 c（用于 mflag 判断条件）
            bool mflag = true;       // true 表示上次使用了二分法

            for (int iter = 0; iter < maxIter; iter++)
            {
                // 根据三点是否共线选择插值方式
                if (Math.Abs(fa - fc) > 1e-15 && Math.Abs(fb - fc) > 1e-15)
                    // 逆二次插值（三点不共线时使用）
                    s = a * fb * fc / ((fa - fb) * (fa - fc))
                      + b * fa * fc / ((fb - fa) * (fb - fc))
                      + c * fa * fb / ((fc - fa) * (fc - fb));
                else
                    // 割线法（两点共线时）
                    s = b - fb * (b - a) / (fb - fa);

                // Brent 方法的五个退化为二分法的判断条件
                bool cond1 = !((3.0 * a + b) / 4.0 < s && s < b || b < s && s < (3.0 * a + b) / 4.0);
                bool cond2 = mflag && Math.Abs(s - b) >= Math.Abs(b - c) / 2.0;
                bool cond3 = !mflag && Math.Abs(s - b) >= Math.Abs(c - d) / 2.0;
                bool cond4 = mflag && Math.Abs(b - c) < tol;
                bool cond5 = !mflag && Math.Abs(c - d) < tol;

                if (cond1 || cond2 || cond3 || cond4 || cond5)
                { s = (a + b) / 2.0; mflag = true; }   // 退化为二分法
                else mflag = false;

                fs = f(s);
                d = c; c = b; fc = fb;                  // 更新历史点

                // 更新区间使 f(a)*f(b) 保持异号
                if (fa * fs < 0) { b = s; fb = fs; }
                else { a = s; fa = fs; }

                // 确保 |f(b)| <= |f(a)|（b 始终是当前最优估计）
                if (Math.Abs(fa) < Math.Abs(fb))
                {
                    double tmp = a; a = b; b = tmp;
                    double ftmp = fa; fa = fb; fb = ftmp;
                }

                // 收敛判断
                if (Math.Abs(fb) < tol || Math.Abs(b - a) < tol) return b;
            }
            return b;  // 达到最大迭代数，返回当前最优估计
        }

        /// <summary>
        /// 对有序数组 xs 和对应值数组 ys 进行线性插值，查询 x 处的值。
        /// 若 x 超出范围则夹紧到端点（不外推）。
        /// 使用二分查找定位区间以提高效率。
        /// </summary>
        /// <param name="x">查询点。</param>
        /// <param name="xs">递增排列的自变量数组。</param>
        /// <param name="ys">对应因变量数组（与 xs 等长）。</param>
        /// <returns>线性插值结果。</returns>
        public static double Interp(double x, double[] xs, double[] ys)
        {
            if (xs.Length == 0) throw new ArgumentException("Empty array");
            if (x <= xs[0]) return ys[0];                          // 左端夹紧
            if (x >= xs[xs.Length - 1]) return ys[xs.Length - 1]; // 右端夹紧

            int i = Array.BinarySearch(xs, x);  // 二分查找
            if (i >= 0) return ys[i];            // 恰好命中
            i = ~i;                              // i 为插入位置，xs[i-1] < x < xs[i]
            double t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);  // 插值参数 t∈[0,1]
            return ys[i - 1] + t * (ys[i] - ys[i - 1]);
        }

        /// <summary>
        /// 数值梯度（中心差分，端点使用前向/后向差分）。
        /// 适用于等间距或不等间距节点。
        /// </summary>
        /// <param name="y">因变量数组。</param>
        /// <param name="x">自变量数组（与 y 等长）。</param>
        /// <returns>梯度数组 dy/dx。</returns>
        public static double[] Gradient(double[] y, double[] x)
        {
            int n = y.Length;
            double[] grad = new double[n];
            if (n == 1) { grad[0] = 0; return grad; }

            // 端点使用一阶前向/后向差分
            grad[0] = (y[1] - y[0]) / (x[1] - x[0]);
            grad[n - 1] = (y[n - 1] - y[n - 2]) / (x[n - 1] - x[n - 2]);

            // 内部节点使用二阶中心差分
            for (int i = 1; i < n - 1; i++)
                grad[i] = (y[i + 1] - y[i - 1]) / (x[i + 1] - x[i - 1]);

            return grad;
        }

        /// <summary>
        /// 梯形法则数值积分 ∫ y dx。
        /// 将积分区域分割为若干梯形并求和，精度为 O(h²)。
        /// </summary>
        /// <param name="y">被积函数值数组。</param>
        /// <param name="x">自变量数组（与 y 等长）。</param>
        /// <returns>积分近似值。</returns>
        public static double Trapezoid(double[] y, double[] x)
        {
            double sum = 0;
            // 相邻两点构成一个梯形，面积 = 0.5*(y_i + y_{i+1}) * (x_{i+1} - x_i)
            for (int i = 0; i < x.Length - 1; i++)
                sum += 0.5 * (y[i] + y[i + 1]) * (x[i + 1] - x[i]);
            return sum;
        }
    }
}
