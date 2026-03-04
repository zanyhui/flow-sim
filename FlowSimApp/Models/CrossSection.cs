using System;
using System.Linq;

namespace FlowSim.Models
{
    /// <summary>
    /// 水力断面抽象基类。
    /// <para>
    /// 提供断面几何与水力参数的统一接口，子类需实现：
    /// <list type="bullet">
    ///   <item><see cref="Properties"/>：计算过水面积 A、湿周 P、水力半径 R、水面宽 T；</item>
    ///   <item><see cref="Conveyance"/>：计算输水能力 K；</item>
    ///   <item><see cref="DConveyance_DA"/>、<see cref="DRadius_DA"/>、<see cref="DArea_Dh"/>：各导数，用于隐式格式的雅可比矩阵；</item>
    ///   <item><see cref="GetEquivalentN"/>：计算复式断面等效糙率；</item>
    ///   <item><see cref="ZAt"/>：查询某横坐标处的床底高程。</item>
    /// </list>
    /// </para>
    /// 基类提供摩阻坡度、正常水深等通用计算方法，以及弯曲修正项。
    /// </summary>
    public abstract class CrossSection
    {
        /// <summary>左侧洪泛区曼宁糙率系数。</summary>
        public double NLeft { get; set; }
        /// <summary>主槽曼宁糙率系数。</summary>
        public double NMain { get; set; }
        /// <summary>右侧洪泛区曼宁糙率系数。</summary>
        public double NRight { get; set; }
        /// <summary>左侧洪泛区边界横坐标（m）。</summary>
        public double LeftFpLimit { get; set; } = 0.0;
        /// <summary>右侧洪泛区边界横坐标（m）。</summary>
        public double RightFpLimit { get; set; } = 0.0;
        /// <summary>弯道曲率（1/曲率半径，无量纲）；0 表示直道。</summary>
        public double Curvature { get; set; } = 0.0;
        /// <summary>河床纵坡 S0，可选；正值表示下坡（顺水流方向降低）。</summary>
        public double? BedSlope { get; set; }

        // 缓存上一次计算的水位及对应结果，避免同一水位重复计算（性能优化）
        protected double? _lastHw;
        protected (double A, double P, double R, double T)? _lastRes;

        /// <summary>
        /// 构造函数：初始化糙率、坡度和弯曲度。
        /// </summary>
        /// <param name="n">统一糙率（同时赋给主槽、左右滩区）。</param>
        /// <param name="bedSlope">河床纵坡（可选）。</param>
        /// <param name="curvature">弯道曲率（可选，默认 0 表示直道）。</param>
        protected CrossSection(double n = 0.03, double? bedSlope = null, double curvature = 0.0)
        {
            NLeft = NMain = NRight = n;   // 初始化时三个区域使用相同糙率
            BedSlope = bedSlope;
            Curvature = curvature;
        }

        /// <summary>断面最低点高程 ZMin（m），即床底高程。子类实现。</summary>
        public abstract double ZMin { get; }

        /// <summary>断面代表宽度（m），如主槽底宽或整体宽度。子类实现。</summary>
        public abstract double Width { get; }

        /// <summary>
        /// 计算给定水位 hw 处的断面几何参数。
        /// </summary>
        /// <param name="hw">绝对水位（m），即河底高程 + 水深。</param>
        /// <returns>(A 过水面积 m², P 湿周 m, R 水力半径 m, T 水面宽 m)。</returns>
        public abstract (double A, double P, double R, double T) Properties(double hw);

        /// <summary>获取给定水位处的等效曼宁糙率（考虑复式断面加权）。</summary>
        /// <param name="hw">绝对水位（m）。</param>
        /// <returns>等效糙率系数 n（无量纲）。</returns>
        public abstract double GetEquivalentN(double hw);

        /// <summary>计算给定水位处的输水能力 K = A·R^(2/3)/n。</summary>
        /// <param name="hw">绝对水位（m）。</param>
        /// <returns>K（m³/s）。</returns>
        public abstract double Conveyance(double hw);

        /// <summary>计算 dK/dA（输水能力对面积的导数），用于雅可比矩阵。</summary>
        public abstract double DConveyance_DA(double hw);

        /// <summary>计算 dR/dA（水力半径对面积的导数），用于链式法则展开。</summary>
        public abstract double DRadius_DA(double hw);

        /// <summary>计算 dA/dh（面积对水深的导数），即水面宽 T。</summary>
        public abstract double DArea_Dh(double hw);

        /// <summary>查询横坐标 x 处的床底高程 z（m），用于不规则断面插值。</summary>
        public abstract double ZAt(double x);

        // 以下为通过 Properties() 派生的便捷属性
        /// <summary>过水面积 A（m²）。</summary>
        public double Area(double hw) => Properties(hw).A;
        /// <summary>湿周 P（m）。</summary>
        public double WettedPerimeter(double hw) => Properties(hw).P;
        /// <summary>水力半径 R = A/P（m）。</summary>
        public double HydraulicRadius(double hw) => Properties(hw).R;
        /// <summary>水面宽 T（m）。</summary>
        public double TopWidth(double hw) => Properties(hw).T;

        /// <summary>
        /// 计算摩阻坡度 Sf = Q·|Q| / K²，其中 K 为当前水位下的输水能力。
        /// </summary>
        /// <param name="h">水深（m）。</param>
        /// <param name="Q">流量（m³/s）。</param>
        /// <returns>摩阻坡度 Sf（无量纲）。</returns>
        public virtual double FrictionSlope(double h, double Q)
        {
            double hw = h + ZMin;                      // 水深转换为绝对水位
            double K = Conveyance(hw);
            return Hydraulics.FrictionSlope(Q, K);
        }

        /// <summary>计算摩阻坡度对过水面积的偏导数 dSf/dA。</summary>
        public virtual double DFrictionSlope_DA(double h, double Q)
        {
            double hw = h + ZMin;
            double K = Conveyance(hw);
            double dK = DConveyance_DA(hw);
            return Hydraulics.DFrictionSlope_DA(Q, K, dK);
        }

        /// <summary>计算摩阻坡度对流量的偏导数 dSf/dQ。</summary>
        public virtual double DFrictionSlope_DQ(double h, double Q)
        {
            double hw = h + ZMin;
            double K = Conveyance(hw);
            return Hydraulics.DFrictionSlope_DQ(Q, K);
        }

        /// <summary>
        /// 计算弯曲坡度 Sc（弯道修正项）。
        /// 当 Curvature == 0 时直接返回 0（直道无修正）。
        /// </summary>
        /// <param name="h">水深（m）。</param>
        /// <param name="Q">流量（m³/s）。</param>
        /// <returns>弯曲坡度 Sc（无量纲）。</returns>
        public double CurvatureSlope(double h, double Q)
        {
            if (Curvature == 0) return 0.0;   // 直道无弯道修正
            double hw = h + ZMin;
            double n = GetEquivalentN(hw);
            var (A, P, R, T) = Properties(hw);
            // rc = 1/Curvature 为弯道曲率半径
            return Hydraulics.CurvatureSlope(h, T, A, Q, n, R, 1.0 / Curvature);
        }

        /// <summary>弯曲坡度对过水面积的偏导数 dSc/dA。</summary>
        public double DCurvatureSlope_DA(double h, double Q)
        {
            if (Math.Abs(Curvature) <= 1e-12) return 0.0;   // 曲率为零直接跳过
            double hw = h + ZMin;
            double n = GetEquivalentN(hw);
            var (A, P, R, T) = Properties(hw);
            double dR_dA = DRadius_DA(hw);
            return Hydraulics.DCurvatureSlope_DA(h, A, Q, n, R, 1.0 / Curvature, dR_dA, T) * DArea_Dh(hw);
        }

        /// <summary>弯曲坡度对流量的偏导数 dSc/dQ。</summary>
        public double DCurvatureSlope_DQ(double h, double Q)
        {
            if (Math.Abs(Curvature) <= 1e-12) return 0.0;
            double hw = h + ZMin;
            double n = GetEquivalentN(hw);
            var (A, P, R, T) = Properties(hw);
            return Hydraulics.DCurvatureSlope_DQ(h, T, A, Q, n, R, 1.0 / Curvature);
        }

        /// <summary>
        /// 计算给定绝对水位 hw 处的正常流量 Qn = K * sqrt(S0)。
        /// 若坡度未定义或为非正值则返回 0。
        /// </summary>
        /// <param name="hw">绝对水位（m）。</param>
        /// <returns>正常流量 Qn（m³/s）。</returns>
        public double NormalFlow(double hw)
        {
            if (BedSlope == null || BedSlope <= 0.0) return 0.0;
            double K = Conveyance(hw);
            return Hydraulics.NormalFlow(BedSlope.Value, K);
        }

        /// <summary>
        /// 使用 Brent 方法求解给定目标流量 Qtarget 对应的正常水深。
        /// 在区间 [ZMin+1e-6, hwMax] 内查找使 Qn(h) = Qtarget 的水深 h。
        /// </summary>
        /// <param name="Qtarget">目标流量（m³/s）。</param>
        /// <param name="hwMax">搜索上限水位（m），默认 ZMin + 100。</param>
        /// <returns>正常水深 h（m，相对于床底）。</returns>
        public double NormalDepth(double Qtarget, double hwMax = double.NaN)
        {
            double zMin = ZMin;
            if (double.IsNaN(hwMax)) hwMax = zMin + 100.0;  // 默认搜索范围 100 m
            // 目标方程：Qtarget - Qn(hw) = 0
            double F(double hw) => Qtarget - NormalFlow(hw);
            try
            {
                double hw = Hydraulics.Brentq(F, zMin + 1e-6, hwMax);
                return hw - zMin;   // 转换回相对水深
            }
            catch
            {
                // 若 F(下限) < 0，说明目标流量极小，返回近似 0
                if (F(zMin + 1e-6) < 0) return 0.0;
                return hwMax - zMin;  // 超出上限，返回最大水深
            }
        }
    }

    /// <summary>
    /// 梯形断面（规则断面）。
    /// <para>
    /// 几何参数：底宽 bMain（m）、边坡系数 mMain（水平:竖直）、床底高程 zBed（m）。
    /// 面积公式：A = (bMain + mMain·h)·h；
    /// 湿周公式：P = bMain + 2h·sqrt(1 + mMain²)；
    /// 水面宽公式：T = bMain + 2·mMain·h。
    /// </para>
    /// </summary>
    public class TrapezoidalSection : CrossSection
    {
        private readonly double _bMain;   // 底宽（m）
        private readonly double _mMain;   // 边坡系数（水平/竖直）
        private readonly double _zBed;    // 床底高程（m）

        /// <summary>
        /// 构造梯形断面。
        /// </summary>
        /// <param name="bMain">底宽（m）。</param>
        /// <param name="mMain">边坡系数（1:mMain，即每升高 1 m 水平扩展 mMain m；矩形为 0）。</param>
        /// <param name="zBed">床底高程（m）。</param>
        /// <param name="nMain">曼宁糙率系数。</param>
        /// <param name="bedSlope">河床纵坡（可选）。</param>
        /// <param name="curvature">弯道曲率（可选，默认 0）。</param>
        public TrapezoidalSection(double bMain, double mMain, double zBed, double nMain,
                                   double? bedSlope = null, double curvature = 0.0)
            : base(nMain, bedSlope, curvature)
        {
            _bMain = bMain;
            _mMain = mMain;
            _zBed = zBed;
        }

        /// <summary>床底高程 = zBed。</summary>
        public override double ZMin => _zBed;

        /// <summary>代表宽度 = 底宽 bMain。</summary>
        public override double Width => _bMain;

        /// <summary>
        /// 计算给定水位 hw 处的梯形断面几何参数。
        /// 当水位低于床底时，所有参数均为 0。
        /// </summary>
        /// <param name="hw">绝对水位（m）。</param>
        /// <returns>(A, P, R, T) 四元组。</returns>
        public override (double A, double P, double R, double T) Properties(double hw)
        {
            double h = hw - _zBed;           // 相对水深（m）
            if (h <= 0) return (0, 0, 0, 0); // 干断面返回零

            double T = _bMain + 2.0 * _mMain * h;                          // 水面宽 T = bMain + 2·mMain·h
            double A = (_bMain + _mMain * h) * h;                          // 过水面积 A = (b + m·h)·h
            double P = _bMain + 2.0 * h * Math.Sqrt(1.0 + _mMain * _mMain); // 湿周 P = b + 2h·sqrt(1+m²)
            double R = P > 0 ? A / P : 0;                                  // 水力半径 R = A/P
            return (A, P, R, T);
        }

        /// <summary>梯形断面等效糙率即主槽糙率 NMain（均匀断面无分区）。</summary>
        public override double GetEquivalentN(double hw) => NMain;

        /// <summary>计算输水能力 K = A·R^(2/3)/n。</summary>
        public override double Conveyance(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (A <= 0) return 0;
            return Hydraulics.Conveyance(A, NMain, R);
        }

        /// <summary>计算 dK/dA，用于雅可比矩阵中的摩阻项展开。</summary>
        public override double DConveyance_DA(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (A <= 0) return 0;
            double dR_dA = DRadius_DA(hw);
            return Hydraulics.DConveyance_DA(A, NMain, R, dR_dA);
        }

        /// <summary>
        /// 计算水力半径对面积的导数 dR/dA = (P - A·dP/dA) / P²。
        /// 推导：R = A/P，dR/dA = (P - A·dP/dA) / P²；
        /// 梯形断面 dP/dA = dP/dh / (dA/dh) = [2·sqrt(1+m²)] / T。
        /// </summary>
        public override double DRadius_DA(double hw)
        {
            double h = hw - _zBed;
            if (h <= 0) return 0;

            double T = _bMain + 2.0 * _mMain * h;
            double A = (_bMain + _mMain * h) * h;
            double P = _bMain + 2.0 * h * Math.Sqrt(1.0 + _mMain * _mMain);
            double dP_dh = 2.0 * Math.Sqrt(1.0 + _mMain * _mMain);  // dP/dh
            double dA_dh = T;                                          // dA/dh = T（水面宽）
            double dP_dA = dP_dh / dA_dh;                             // 链式法则：dP/dA = (dP/dh)/(dA/dh)
            return (P - A * dP_dA) / (P * P);                         // 商法则
        }

        /// <summary>
        /// 计算面积对水深的导数 dA/dh = T（水面宽）。
        /// 梯形断面：T = bMain + 2·mMain·h；当 h≤0 时取底宽 bMain。
        /// </summary>
        public override double DArea_Dh(double hw)
        {
            double h = hw - _zBed;
            if (h <= 0) return _bMain;                       // 干断面近似用底宽
            return _bMain + 2.0 * _mMain * h;               // 水面宽
        }

        /// <summary>梯形断面床底高程处处为 _zBed，与横坐标 x 无关。</summary>
        public override double ZAt(double x) => _zBed;
    }

    /// <summary>
    /// 不规则断面，由一组横坐标-高程（x, z）折线点定义。
    /// <para>
    /// 支持复式断面（主槽 + 左右洪泛区），通过 <see cref="LeftFpLimit"/> 和
    /// <see cref="RightFpLimit"/> 划分分区，采用 Lotter（Horton-Einstein）
    /// 复合输水能力方法计算等效糙率：
    /// K_total = (K_left^1.5 + K_main^1.5 + K_right^1.5)^(2/3)。
    /// </para>
    /// 断面几何参数（A、P、T）通过对湿周线段分段积分/求和计算。
    /// </summary>
    public class IrregularSection : CrossSection
    {
        /// <summary>断面横坐标数组（m），按升序排列。</summary>
        public double[] X { get; private set; }
        /// <summary>断面高程数组（m），与 X 对应。</summary>
        public double[] Z { get; private set; }

        private readonly double _zMin;    // 最低点高程（床底）
        private readonly double _width;   // 总宽度 = X.Max - X.Min

        /// <summary>
        /// 构造不规则断面。
        /// </summary>
        /// <param name="x">横坐标数组（m），无需预先排序。</param>
        /// <param name="z">对应高程数组（m）。</param>
        /// <param name="n">初始统一糙率。</param>
        /// <param name="bedSlope">河床纵坡（可选）。</param>
        /// <param name="curvature">弯道曲率（可选）。</param>
        public IrregularSection(double[] x, double[] z, double n = 0.03,
                                 double? bedSlope = null, double curvature = 0.0)
            : base(n, bedSlope, curvature)
        {
            // 按横坐标升序排列断面点
            int[] idx = Enumerable.Range(0, x.Length).OrderBy(i => x[i]).ToArray();
            X = idx.Select(i => x[i]).ToArray();
            Z = idx.Select(i => z[i]).ToArray();
            _zMin = Z.Min();                           // 最低床底高程
            _width = X.Max() - X.Min();               // 断面总宽度
            LeftFpLimit = X[0];                        // 默认无分区：左右滩区界限设为断面端点
            RightFpLimit = X[X.Length - 1];
        }

        /// <summary>最低点高程 = min(Z)。</summary>
        public override double ZMin => _zMin;

        /// <summary>断面总宽度 = X.Max - X.Min。</summary>
        public override double Width => _width;

        /// <summary>
        /// 计算给定水位 hw 处的不规则断面几何参数。
        /// <para>
        /// 算法：
        /// 1. 扫描折线段找出水线以下的湿润区段（FindWetSegments）；
        /// 2. 对每个湿润区段，通过插值确定水线与折线的交叉点（BuildSegment）；
        /// 3. 对每段用梯形积分计算面积 A，用勾股定理计算湿周 P，
        ///    用端点距计算水面宽 T；
        /// 4. 汇总所有区段得到总 A、P、T 和 R。
        /// </para>
        /// </summary>
        /// <param name="hw">绝对水位（m）。</param>
        /// <returns>(A, P, R, T) 四元组。</returns>
        public override (double A, double P, double R, double T) Properties(double hw)
        {
            // 命中缓存，直接返回（避免重复计算）
            if (_lastHw.HasValue && _lastHw.Value == hw && _lastRes.HasValue)
                return _lastRes.Value;

            // 水位低于最低点，断面为干
            if (hw <= _zMin)
            {
                var r = (0.0, 0.0, 0.0, 0.0);
                _lastHw = hw; _lastRes = r;
                return r;
            }

            // 计算各折线节点处的水深（水位 - 节点高程）
            double[] hNodes = Z.Select(z => hw - z).ToArray();
            if (!hNodes.Any(h => h > 0))   // 所有节点均在水面以上，断面为干
            {
                var r = (0.0, 0.0, 0.0, 0.0);
                _lastHw = hw; _lastRes = r;
                return r;
            }

            // 查找湿润区段（连续的 hNodes > 0 的折线段范围）
            var wetSegments = FindWetSegments(hNodes);

            double A_total = 0, P_total = 0, T_total = 0;

            foreach (var (i0, iN) in wetSegments)
            {
                // 构造该湿润区段的 x、z 折线（包含与水面的交点）
                var (xSeg, zSeg) = BuildSegment(i0, iN, hw);
                // 各节点处的水深 d_k = max(hw - z_k, 0)
                double[] dSeg = zSeg.Select(z => Math.Max(hw - z, 0.0)).ToArray();

                // 梯形积分求面积：A = Σ 0.5*(d_k + d_{k+1})*(x_{k+1} - x_k)
                double A = 0;
                for (int k = 0; k < xSeg.Length - 1; k++)
                    A += 0.5 * (dSeg[k] + dSeg[k + 1]) * (xSeg[k + 1] - xSeg[k]);

                // 勾股定理求湿周：P = Σ sqrt(dx² + dz²)
                double P = 0;
                for (int k = 0; k < xSeg.Length - 1; k++)
                {
                    double dx = xSeg[k + 1] - xSeg[k];
                    double dz = zSeg[k + 1] - zSeg[k];
                    P += Math.Sqrt(dx * dx + dz * dz);
                }

                // 水面宽 T = 最右端 - 最左端横坐标
                double T = xSeg[xSeg.Length - 1] - xSeg[0];

                A_total += A;
                P_total += P;
                T_total += T;
            }

            double R_total = P_total > 0 ? A_total / P_total : 0.0;  // 水力半径 R = A/P
            var res = (A_total, P_total, R_total, T_total);
            // 更新缓存
            _lastHw = hw; _lastRes = res;
            return res;
        }

        /// <summary>
        /// 在折线节点水深数组 hNodes 中，查找所有连续湿润区段（hNodes[i] > 0 的连续范围）。
        /// </summary>
        /// <param name="hNodes">各折线节点处的水深（正值表示湿润）。</param>
        /// <returns>各湿润区段的起止节点索引列表 (start, end)。</returns>
        private System.Collections.Generic.List<(int start, int end)> FindWetSegments(double[] hNodes)
        {
            var segs = new System.Collections.Generic.List<(int, int)>();
            int i = 0, n = hNodes.Length;
            while (i < n)
            {
                if (hNodes[i] > 0)
                {
                    int start = i;
                    // 向右扩展直到遇到干节点或数组末尾
                    while (i + 1 < n && hNodes[i + 1] > 0) i++;
                    segs.Add((start, i));
                }
                i++;
            }
            return segs;
        }

        /// <summary>
        /// 根据湿润区段的起止节点索引，构造包含水面交点的折线段。
        /// <para>
        /// 若区段两端存在干节点（高程 > hw），则通过线性插值确定水面与折线的交叉点，
        /// 并插入到折线段的两端，以准确描述湿润边界。
        /// </para>
        /// </summary>
        /// <param name="i0">湿润区段起始节点索引。</param>
        /// <param name="iN">湿润区段终止节点索引。</param>
        /// <param name="hw">水位（m）。</param>
        /// <returns>(xSeg, zSeg)：区段的横坐标和高程数组。</returns>
        private (double[] xSeg, double[] zSeg) BuildSegment(int i0, int iN, double hw)
        {
            var xList = new System.Collections.Generic.List<double>();
            var zList = new System.Collections.Generic.List<double>();

            // 若左侧相邻节点高于水面，则插值确定左侧水线交点
            if (i0 > 0 && Z[i0 - 1] > hw)
            {
                double t = (hw - Z[i0 - 1]) / (Z[i0] - Z[i0 - 1]);   // 线性插值参数
                double xl = X[i0 - 1] + t * (X[i0] - X[i0 - 1]);     // 交点横坐标
                xList.Add(xl); zList.Add(hw);                           // 交点高程恰为水位
            }

            // 加入区段内的所有节点
            for (int k = i0; k <= iN; k++) { xList.Add(X[k]); zList.Add(Z[k]); }

            // 若右侧相邻节点高于水面，则插值确定右侧水线交点
            if (iN < X.Length - 1 && Z[iN + 1] > hw)
            {
                double t = (hw - Z[iN]) / (Z[iN + 1] - Z[iN]);
                double xr = X[iN] + t * (X[iN + 1] - X[iN]);
                xList.Add(xr); zList.Add(hw);
            }

            return (xList.ToArray(), zList.ToArray());
        }

        /// <summary>
        /// 计算给定水位处的等效曼宁糙率（复式断面 Lotter 方法）。
        /// <para>
        /// Lotter（Horton-Einstein）方法：
        /// 将断面分为左滩区、主槽、右滩区三个子断面，
        /// 各子断面分别计算输水能力 K_i，再按下式合成：
        /// K_total = (K_left^1.5 + K_main^1.5 + K_right^1.5)^(2/3)
        /// 最后反算等效糙率 n_eq = A·R^(2/3) / K_total。
        /// </para>
        /// </summary>
        /// <param name="hw">绝对水位（m）。</param>
        /// <returns>等效曼宁糙率系数 n_eq（无量纲）。</returns>
        public override double GetEquivalentN(double hw)
        {
            double A_total = Area(hw);
            double P_total = WettedPerimeter(hw);
            if (A_total <= 0 || P_total <= 0) return NMain;
            double R_total = A_total / P_total;

            // 辅助函数：计算指定横坐标范围内的子断面输水能力
            double GetSubsectionK(double xMin, double xMax, double n)
            {
                // 筛选位于 [xMin, xMax] 范围内的节点索引
                var mask = X.Select((xi, i) => (xi, i)).Where(t => t.xi >= xMin && t.xi <= xMax).Select(t => t.i).ToArray();
                if (mask.Length < 2) return 0;
                var xs = CreateSubSection(X, Z, mask, n);
                double A = xs.Area(hw);
                if (A <= 0) return 0;
                double P = xs.WettedPerimeter(hw);
                if (P <= 0) return 0;
                double R = A / P;
                return Hydraulics.Conveyance(A, n, R);
            }

            double KLeft  = GetSubsectionK(X[0],              LeftFpLimit,       NLeft);   // 左滩区
            double KMain  = GetSubsectionK(LeftFpLimit,        RightFpLimit,      NMain);   // 主槽
            double KRight = GetSubsectionK(RightFpLimit,       X[X.Length - 1],  NRight);  // 右滩区

            // Lotter 公式：K_total = (K_L^1.5 + K_M^1.5 + K_R^1.5)^(2/3)
            double K_total = Math.Pow(Math.Pow(KLeft, 1.5) + Math.Pow(KMain, 1.5) + Math.Pow(KRight, 1.5), 2.0 / 3.0);
            if (K_total <= 0) return NMain;

            // 反算等效糙率 n_eq = A·R^(2/3) / K_total
            return A_total * Math.Pow(R_total, 2.0 / 3.0) / K_total;
        }

        /// <summary>
        /// 从指定索引集合中提取子断面，构造 IrregularSection 实例。
        /// </summary>
        private static IrregularSection CreateSubSection(double[] X, double[] Z, int[] indices, double n)
        {
            double[] xs = indices.Select(i => X[i]).ToArray();
            double[] zs = indices.Select(i => Z[i]).ToArray();
            return new IrregularSection(xs, zs, n);
        }

        /// <summary>计算输水能力 K = A·R^(2/3)/n_eq，使用 Lotter 等效糙率。</summary>
        public override double Conveyance(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (A <= 0) return 0;
            double n = GetEquivalentN(hw);
            return Hydraulics.Conveyance(A, n, R);
        }

        /// <summary>计算 dK/dA，使用等效糙率。</summary>
        public override double DConveyance_DA(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (A <= 0) return 0;
            double n = GetEquivalentN(hw);
            double dR_dA = DRadius_DA(hw);
            return Hydraulics.DConveyance_DA(A, n, R, dR_dA);
        }

        /// <summary>
        /// 计算 dR/dA，使用数值差分估算 dP/dh，再通过链式法则求 dR/dA。
        /// 数值差分步长 dh = 1e-4 m（足够小以保证精度，但不至于引起数值误差）。
        /// </summary>
        public override double DRadius_DA(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (P <= 0 || T <= 0) return 0;
            double dh = 1e-4;                           // 数值差分步长
            double P2 = Properties(hw + dh).P;         // 水位微增后的湿周
            double dP_dh = (P2 - P) / dh;              // 数值导数 dP/dh
            double dP_dA = dP_dh / T;                  // 链式法则：dP/dA = (dP/dh)/(dA/dh) = dP/dh / T
            return (P - A * dP_dA) / (P * P);          // dR/dA = (P - A·dP/dA) / P²
        }

        /// <summary>dA/dh = T（水面宽），即面积对水深的导数等于水面宽。</summary>
        public override double DArea_Dh(double hw)
        {
            return Properties(hw).T;
        }

        /// <summary>通过线性插值查询横坐标 x 处的床底高程 z（m）。</summary>
        public override double ZAt(double x)
        {
            return Hydraulics.Interp(x, X, Z);
        }
    }

    /// <summary>
    /// 断面插值辅助工具类：在两个断面之间按距离权重线性插值，生成中间断面。
    /// <para>
    /// 权重规则：距左断面 dist1、距右断面 dist2，则：
    ///   w1 = dist2 / (dist1 + dist2)（右距离权左断面）
    ///   w2 = dist1 / (dist1 + dist2)（左距离权右断面）
    /// 这使得靠近哪个断面、权重就越大。
    /// </para>
    /// </summary>
    public static class CrossSectionInterpolator
    {
        /// <summary>
        /// 在两个断面 xs1 和 xs2 之间按距离插值，返回中间断面。
        /// 仅支持同类型断面（梯形/不规则）的插值；类型不同时退回到返回 xs1。
        /// </summary>
        /// <param name="xs1">左侧（上游）断面。</param>
        /// <param name="xs2">右侧（下游）断面。</param>
        /// <param name="dist1">插值点距 xs1 的距离（m）。</param>
        /// <param name="dist2">插值点距 xs2 的距离（m）。</param>
        /// <returns>插值生成的中间断面。</returns>
        public static CrossSection Interpolate(CrossSection xs1, CrossSection xs2, double dist1, double dist2)
        {
            double totalDist = dist1 + dist2;
            if (totalDist <= 0) return xs1;   // 距离为零直接返回左侧断面

            // 权重：离哪个断面近，权重越大
            double w1 = dist2 / totalDist;    // xs1 的权重（由 dist2 决定）
            double w2 = dist1 / totalDist;    // xs2 的权重（由 dist1 决定）

            if (xs1 is TrapezoidalSection t1 && xs2 is TrapezoidalSection t2)
            {
                // 梯形断面：对底宽 b、床底高程 z、糙率 n、坡度 S0 线性插值
                double b = t1.Width * w1 + t2.Width * w2;
                double z = t1.ZMin * w1 + t2.ZMin * w2;
                double n = t1.NMain * w1 + t2.NMain * w2;
                double S0 = (t1.BedSlope.HasValue && t2.BedSlope.HasValue)
                    ? t1.BedSlope.Value * w1 + t2.BedSlope.Value * w2
                    : (t1.BedSlope ?? t2.BedSlope) ?? 0;
                return new TrapezoidalSection(b, 0, z, n, S0);
            }
            else if (xs1 is IrregularSection ir1 && xs2 is IrregularSection ir2)
            {
                // 不规则断面：保持 xs1 的横坐标，对各节点高程插值
                double[] x = ir1.X;
                double[] z = new double[x.Length];
                for (int i = 0; i < x.Length; i++)
                {
                    // 在 xs2 中插值得到对应横坐标 x[i] 处的高程
                    double z2 = Hydraulics.Interp(x[i], ir2.X, ir2.Z);
                    z[i] = ir1.Z[i] * w1 + z2 * w2;   // 加权插值
                }
                double n = ir1.NMain * w1 + ir2.NMain * w2;
                double S0 = (ir1.BedSlope.HasValue && ir2.BedSlope.HasValue)
                    ? ir1.BedSlope.Value * w1 + ir2.BedSlope.Value * w2
                    : (ir1.BedSlope ?? ir2.BedSlope) ?? 0;
                return new IrregularSection(x, z, n, S0);
            }
            else
            {
                // 类型不一致，退回到左侧断面
                return xs1;
            }
        }
    }
}
