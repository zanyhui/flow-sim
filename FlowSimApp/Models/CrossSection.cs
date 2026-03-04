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

        /// <summary>获取糙率参数元组 (NLeft, NMain, NRight, LeftFpLimit, RightFpLimit)。</summary>
        public (double nLeft, double nMain, double nRight, double leftFpLimit, double rightFpLimit) GetRoughnessPara()
            => (NLeft, NMain, NRight, LeftFpLimit, RightFpLimit);

        /// <summary>批量设置糙率参数 (NLeft, NMain, NRight, LeftFpLimit, RightFpLimit)。</summary>
        public void SetRoughnessPara((double nLeft, double nMain, double nRight, double leftFpLimit, double rightFpLimit) parameters)
        {
            NLeft        = parameters.nLeft;
            NMain        = parameters.nMain;
            NRight       = parameters.nRight;
            LeftFpLimit  = parameters.leftFpLimit;
            RightFpLimit = parameters.rightFpLimit;
        }
    }

    /// <summary>
    /// 梯形断面（规则断面），支持简单矩形、简单梯形和复式梯形（主槽 + 左右滩区）。
    /// <para>
    /// 简单模式几何：底宽 bMain（m）、边坡系数 mMain（水平:竖直）、床底高程 zBed（m）。
    /// 复式模式需额外提供滩面高程 zBank、滩区底宽（bFpLeft/bFpRight）和滩区边坡 mFp。
    /// </para>
    /// </summary>
    public class TrapezoidalSection : CrossSection
    {
        private readonly double _bMain;   // 底宽（m）
        private readonly double _mMain;   // 边坡系数（水平/竖直）
        private readonly double _zBed;    // 床底高程（m）

        // 复式断面参数
        private readonly bool   _isCompound;      // 是否为复式断面
        private readonly bool   _isRect;           // 是否为矩形断面（m=0 且非复式）
        private readonly double _zBank;            // 滩面高程（m）
        private readonly double _bFpLeft;          // 左侧滩区底宽（m）
        private readonly double _bFpRight;         // 右侧滩区底宽（m）
        private readonly double _mFp;              // 滩区外侧边坡系数
        private readonly double _bankfullDepth;    // 平滩水深 = zBank - zBed
        private readonly double _tMainAtBank;      // 平滩时主槽水面宽
        private readonly double _widthAtBank;      // 平滩时总断面宽（含滩区）

        /// <summary>边坡系数（水平/竖直）。</summary>
        public double MMain => _mMain;
        /// <summary>底宽（m）。</summary>
        public double BMain => _bMain;
        /// <summary>是否为复式断面。</summary>
        public bool IsCompound => _isCompound;
        /// <summary>滩面高程（m），仅复式断面有效。</summary>
        public double ZBank => _zBank;
        /// <summary>左侧滩区底宽（m）。</summary>
        public double BFpLeft => _bFpLeft;
        /// <summary>右侧滩区底宽（m）。</summary>
        public double BFpRight => _bFpRight;
        /// <summary>滩区外侧边坡系数。</summary>
        public double MFp => _mFp;

        /// <summary>
        /// 构造梯形断面（支持简单矩形/梯形和复式梯形）。
        /// </summary>
        /// <param name="bMain">底宽（m）。</param>
        /// <param name="mMain">边坡系数（1:mMain；矩形为 0）。</param>
        /// <param name="zBed">床底高程（m）。</param>
        /// <param name="nMain">主槽曼宁糙率系数。</param>
        /// <param name="bedSlope">河床纵坡（可选）。</param>
        /// <param name="curvature">弯道曲率（可选，默认 0）。</param>
        /// <param name="zBank">滩面高程（m）；非 null 时启用复式断面。</param>
        /// <param name="bFpLeft">左侧滩区底宽（m）。</param>
        /// <param name="bFpRight">右侧滩区底宽（m）。</param>
        /// <param name="mFp">滩区外侧边坡系数。</param>
        /// <param name="nLeft">左滩区曼宁糙率。</param>
        /// <param name="nRight">右滩区曼宁糙率。</param>
        public TrapezoidalSection(double bMain, double mMain, double zBed, double nMain,
                                   double? bedSlope = null, double curvature = 0.0,
                                   double? zBank = null, double bFpLeft = 0.0, double bFpRight = 0.0,
                                   double mFp = 0.0, double nLeft = 0.03, double nRight = 0.03)
            : base(nMain, bedSlope, curvature)
        {
            _bMain = bMain;
            _mMain = mMain;
            _zBed  = zBed;

            if (zBank.HasValue)
            {
                if (zBank.Value <= zBed)
                    throw new ArgumentException("滩面高程 zBank 必须高于床底高程 zBed");

                _isCompound    = true;
                _zBank         = zBank.Value;
                _bFpLeft       = bFpLeft;
                _bFpRight      = bFpRight;
                _mFp           = mFp;
                _bankfullDepth = _zBank - _zBed;
                _tMainAtBank   = _bMain + 2.0 * _mMain * _bankfullDepth;
                LeftFpLimit    = -_tMainAtBank / 2.0;
                RightFpLimit   =  _tMainAtBank / 2.0;
                _widthAtBank   = _bFpLeft + _tMainAtBank + _bFpRight;

                NLeft  = nLeft;
                NRight = nRight;
            }
            else
            {
                _isCompound    = false;
                _zBank         = 0;
                _bFpLeft = _bFpRight = _mFp = 0;
                _bankfullDepth = _tMainAtBank = _widthAtBank = 0;

                // 简单断面：左/右滩区糙率与主槽相同
                NLeft = NRight = NMain;
            }

            _isRect = !_isCompound && mMain == 0.0;
        }

        /// <summary>床底高程 = zBed。</summary>
        public override double ZMin => _zBed;

        /// <summary>代表宽度 = 底宽 bMain。</summary>
        public override double Width => _bMain;

        /// <summary>
        /// 计算给定水位 hw 处的断面几何参数，支持三种情形：
        /// 1) 矩形（m=0 且非复式）；2) 简单梯形；3) 复式梯形（含左/右滩区）。
        /// </summary>
        public override (double A, double P, double R, double T) Properties(double hw)
        {
            if (_lastHw.HasValue && _lastHw.Value == hw && _lastRes.HasValue)
                return _lastRes.Value;

            double depth = Math.Max(0.0, hw - _zBed);
            if (depth <= 0.0)
            {
                var r0 = (0.0, 0.0, 0.0, 0.0);
                _lastHw = hw; _lastRes = r0;
                return r0;
            }

            double A, P, T;

            if (_isRect)
            {
                // 矩形断面
                A = _bMain * depth;
                P = _bMain + 2.0 * depth;
                T = _bMain;
            }
            else if (!_isCompound)
            {
                // 简单梯形断面
                T = _bMain + 2.0 * _mMain * depth;
                A = (_bMain + T) / 2.0 * depth;
                P = _bMain + 2.0 * depth * Math.Sqrt(1.0 + _mMain * _mMain);
            }
            else
            {
                // 复式梯形断面
                if (depth <= _bankfullDepth)
                {
                    // 水位低于平滩：只有主槽过水
                    T = _bMain + 2.0 * _mMain * depth;
                    A = (_bMain + T) / 2.0 * depth;
                    P = _bMain + 2.0 * depth * Math.Sqrt(1.0 + _mMain * _mMain);
                }
                else
                {
                    // 水位超过平滩：主槽 + 左/右滩区均过水
                    double depthFp = depth - _bankfullDepth;

                    // 主槽（满槽，仅计算至滩面）
                    double aMain = (_bMain + _tMainAtBank) / 2.0 * _bankfullDepth;
                    double pMain = _bMain + 2.0 * _bankfullDepth * Math.Sqrt(1.0 + _mMain * _mMain);

                    // 左侧滩区（梯形，A = (底宽 + 顶宽)/2 * 高 = (b + b+m*h)/2 * h = (b + 0.5*m*h)*h）
                    double aLeft  = (_bFpLeft  + 0.5 * _mFp * depthFp) * depthFp;
                    double pLeft  = _bFpLeft  + depthFp * Math.Sqrt(1.0 + _mFp * _mFp);

                    // 右侧滩区（梯形）
                    double aRight = (_bFpRight + 0.5 * _mFp * depthFp) * depthFp;
                    double pRight = _bFpRight + depthFp * Math.Sqrt(1.0 + _mFp * _mFp);

                    A = aMain + aLeft + aRight;
                    P = pMain + pLeft + pRight;
                    T = _widthAtBank + 2.0 * _mFp * depthFp;
                }
            }

            double R = P > 0.0 ? A / P : 0.0;
            var res = (A, P, R, T);
            _lastHw = hw; _lastRes = res;
            return res;
        }

        /// <summary>
        /// 获取复式断面各子区（左滩、主槽、右滩）的 (A, P_床底, R) 参数，
        /// 用于 Lotter 组合输水能力计算。简单断面返回 ((0,0,0), (A,P,R), (0,0,0))。
        /// </summary>
        private ((double A, double P, double R) left,
                 (double A, double P, double R) main,
                 (double A, double P, double R) right)
            GetSubsectionProps(double hw)
        {
            double depth = Math.Max(0.0, hw - _zBed);
            if (depth <= 0.0) return ((0, 0, 0), (0, 0, 0), (0, 0, 0));

            if (!_isCompound || depth <= _bankfullDepth)
            {
                var (A, P, R, _) = Properties(hw);
                return ((0, 0, 0), (A, P, R), (0, 0, 0));
            }

            double depthFp = depth - _bankfullDepth;

            // 主槽（含滩面以上矩形延伸部分）
            double aMain    = (_bMain + _tMainAtBank) / 2.0 * _bankfullDepth + _tMainAtBank * depthFp;
            double pMainBed = _bMain + 2.0 * _bankfullDepth * Math.Sqrt(1.0 + _mMain * _mMain);
            double rMain    = pMainBed > 0 ? aMain / pMainBed : 0.0;

            // 左侧滩区
            double aLeft    = (_bFpLeft  + 0.5 * _mFp * depthFp) * depthFp;
            double pLeftBed = _bFpLeft  + depthFp * Math.Sqrt(1.0 + _mFp * _mFp);
            double rLeft    = pLeftBed  > 0 ? aLeft  / pLeftBed  : 0.0;

            // 右侧滩区
            double aRight    = (_bFpRight + 0.5 * _mFp * depthFp) * depthFp;
            double pRightBed = _bFpRight  + depthFp * Math.Sqrt(1.0 + _mFp * _mFp);
            double rRight    = pRightBed  > 0 ? aRight / pRightBed : 0.0;

            return ((aLeft, pLeftBed, rLeft), (aMain, pMainBed, rMain), (aRight, pRightBed, rRight));
        }

        /// <summary>
        /// 等效曼宁糙率：简单断面直接返回 NMain；
        /// 复式断面使用 Lotter（Horton-Einstein）方法合成。
        /// </summary>
        public override double GetEquivalentN(double hw)
        {
            if (!_isCompound) return NMain;

            var (left, main, right) = GetSubsectionProps(hw);
            double kLeft  = Hydraulics.Conveyance(left.A,  NLeft,  left.R);
            double kMain  = Hydraulics.Conveyance(main.A,  NMain,  main.R);
            double kRight = Hydraulics.Conveyance(right.A, NRight, right.R);

            var (aTotal, _, rTotal, _) = Properties(hw);
            if (aTotal <= 0 || rTotal <= 0) return NMain;

            // Lotter（Horton-Einstein）公式：K_total = (K_L^1.5 + K_M^1.5 + K_R^1.5)^(2/3)
            double kTotal = Math.Pow(Math.Pow(kLeft, 1.5) + Math.Pow(kMain, 1.5) + Math.Pow(kRight, 1.5), 2.0 / 3.0);
            if (kTotal <= 0.0) return NMain;

            return aTotal * Math.Pow(rTotal, 2.0 / 3.0) / kTotal;
        }

        /// <summary>
        /// 计算输水能力 K：
        /// 简单断面使用全断面公式；复式断面使用 Lotter 组合输水能力。
        /// </summary>
        public override double Conveyance(double hw)
        {
            if (!_isCompound)
            {
                var (A, P, R, T) = Properties(hw);
                if (A <= 0) return 0;
                return Hydraulics.Conveyance(A, NMain, R);
            }

            var (left, main, right) = GetSubsectionProps(hw);
            double kLeft  = Hydraulics.Conveyance(left.A,  NLeft,  left.R);
            double kMain  = Hydraulics.Conveyance(main.A,  NMain,  main.R);
            double kRight = Hydraulics.Conveyance(right.A, NRight, right.R);
            return Math.Pow(Math.Pow(kLeft, 1.5) + Math.Pow(kMain, 1.5) + Math.Pow(kRight, 1.5), 2.0 / 3.0);
        }

        /// <summary>计算 dK/dA，用于雅可比矩阵中的摩阻项展开。</summary>
        public override double DConveyance_DA(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (A <= 0) return 0;
            double n    = GetEquivalentN(hw);
            double dRdA = DRadius_DA(hw);
            return Hydraulics.DConveyance_DA(A, n, R, dRdA);
        }

        /// <summary>
        /// 计算水力半径对面积的导数 dR/dA，使用解析公式：
        /// dP/dh 根据断面类型（矩形/梯形/复式）推导，dR/dA = (P - A·dP/dA) / P²。
        /// </summary>
        public override double DRadius_DA(double hw)
        {
            double depth = hw - _zBed;
            if (depth <= 0) return 0;

            var (A, P, R, T) = Properties(hw);
            if (P <= 0 || T <= 0) return 0;

            double dP_dh;
            if (_isRect)
                dP_dh = 2.0;
            else if (!_isCompound)
                dP_dh = 2.0 * Math.Sqrt(1.0 + _mMain * _mMain);
            else
                dP_dh = depth <= _bankfullDepth
                    ? 2.0 * Math.Sqrt(1.0 + _mMain * _mMain)
                    : 2.0 * Math.Sqrt(1.0 + _mFp   * _mFp);

            double dP_dA = dP_dh / T;
            return (P - A * dP_dA) / (P * P);
        }

        /// <summary>
        /// 计算面积对水深的导数 dA/dh = T（水面宽）。
        /// h≤0 时返回底宽 bMain。
        /// </summary>
        public override double DArea_Dh(double hw)
        {
            double h = hw - _zBed;
            if (h <= 0) return _bMain;
            return Properties(hw).T;
        }

        /// <summary>
        /// 查询横坐标 x 处的床底高程 z（m）。
        /// 支持矩形、简单梯形和复式梯形三种情形。
        /// </summary>
        public override double ZAt(double x)
        {
            if (_isRect)
                return (x > -_bMain / 2.0 && x < _bMain / 2.0) ? _zBed : double.PositiveInfinity;

            if (!_isCompound)
            {
                // 简单梯形（此时 _mMain != 0）
                if (x >= -_bMain / 2.0 && x <= _bMain / 2.0)
                    return _zBed;
                else if (x > _bMain / 2.0)
                    return _zBed + (x - _bMain / 2.0) / _mMain;
                else
                    return _zBed + (-x - _bMain / 2.0) / _mMain;
            }

            // 复式梯形
            if (x >= LeftFpLimit && x <= RightFpLimit)
            {
                // 主槽内
                if (x >= -_bMain / 2.0 && x <= _bMain / 2.0)
                    return _zBed;
                else if (x > _bMain / 2.0)
                    return _mMain > 0 ? _zBed + (x  - _bMain / 2.0) / _mMain : double.PositiveInfinity;
                else
                    return _mMain > 0 ? _zBed + (-x - _bMain / 2.0) / _mMain : double.PositiveInfinity;
            }
            else if (x < LeftFpLimit)
            {
                // 左侧滩区
                double xLeftBedOuter = LeftFpLimit - _bFpLeft;
                if (x >= xLeftBedOuter)
                    return _zBank;
                else
                    return _mFp > 0 ? _zBank + (xLeftBedOuter - x) / _mFp : double.PositiveInfinity;
            }
            else
            {
                // 右侧滩区
                double xRightBedOuter = RightFpLimit + _bFpRight;
                if (x <= xRightBedOuter)
                    return _zBank;
                else
                    return _mFp > 0 ? _zBank + (x - xRightBedOuter) / _mFp : double.PositiveInfinity;
            }
        }
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
        /// 计算 dR/dA，使用中心差分（步长 dh=1e-6）：
        /// dR/dA = (R(hw+dh) - R(hw-dh)) / (A(hw+dh) - A(hw-dh))。
        /// </summary>
        public override double DRadius_DA(double hw)
        {
            double dh = 1e-6;  // 中心差分步长（相比前向差分 1e-4 精度更高）
            double a1 = Area(hw - dh);
            double a2 = Area(hw + dh);
            if (a2 - a1 == 0.0) return 0.0;
            double r1 = HydraulicRadius(hw - dh);
            double r2 = HydraulicRadius(hw + dh);
            return (r2 - r1) / (a2 - a1);
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

        /// <summary>
        /// 识别断面中连续的湿润子槽（subchannel）。
        /// 对每段湿润区域，用插值确定与水面的交叉点，返回各子槽的 (x, z) 折线数组。
        /// </summary>
        private (double[] x, double[] z)[] GetSubchannels(double hw)
        {
            bool[] wet = Z.Select(zi => zi < hw).ToArray();
            var subchannels = new System.Collections.Generic.List<(double[], double[])>();
            int i = 0, n = wet.Length;

            while (i < n)
            {
                if (!wet[i]) { i++; continue; }

                int start = i;
                while (i < n && wet[i]) i++;
                int end = i; // one past last wet index

                if (end - start < 2) continue;

                var xList = new System.Collections.Generic.List<double>();
                var zList = new System.Collections.Generic.List<double>();

                // 左侧与水面的交叉点（插值）
                if (start > 0 && Z[start - 1] > hw)
                {
                    double t = (hw - Z[start - 1]) / (Z[start] - Z[start - 1]);
                    xList.Add(X[start - 1] + t * (X[start] - X[start - 1]));
                    zList.Add(hw);
                }

                for (int k = start; k < end; k++) { xList.Add(X[k]); zList.Add(Z[k]); }

                // 右侧与水面的交叉点（插值）
                if (end < n && Z[end - 1] < hw && Z[end] > hw)
                {
                    double t = (hw - Z[end - 1]) / (Z[end] - Z[end - 1]);
                    xList.Add(X[end - 1] + t * (X[end] - X[end - 1]));
                    zList.Add(hw);
                }

                subchannels.Add((xList.ToArray(), zList.ToArray()));
            }

            return subchannels.ToArray();
        }

        /// <summary>
        /// 计算摩阻坡度 Sf，支持多子槽（Horton-Einstein 组合输水能力方法）。
        /// 单子槽时回退至基类实现。
        /// </summary>
        public override double FrictionSlope(double h, double Q)
        {
            double hw = h + ZMin;
            var subchs = GetSubchannels(hw);
            if (subchs.Length <= 1)
                return base.FrictionSlope(h, Q);

            double kSum = 0.0;
            var roughness = GetRoughnessPara();
            foreach (var (xSeg, zSeg) in subchs)
            {
                var xs = new IrregularSection(xSeg, zSeg);
                xs.SetRoughnessPara(roughness);
                double kJ = xs.Conveyance(hw);
                kSum += Math.Pow(kJ, 1.5);
            }
            double kTotal = Math.Pow(kSum, 2.0 / 3.0);
            return Hydraulics.FrictionSlope(Q, kTotal);
        }

        /// <summary>
        /// 计算 dSf/dA，支持多子槽（组合输水能力导数）。
        /// 单子槽时回退至基类实现。
        /// </summary>
        public override double DFrictionSlope_DA(double h, double Q)
        {
            double hw = h + ZMin;
            var subchs = GetSubchannels(hw);
            if (subchs.Length <= 1)
                return base.DFrictionSlope_DA(h, Q);

            double kSum = 0.0, dkSum = 0.0;
            var roughness = GetRoughnessPara();
            foreach (var (xSeg, zSeg) in subchs)
            {
                var xs = new IrregularSection(xSeg, zSeg);
                xs.SetRoughnessPara(roughness);
                double kJ  = xs.Conveyance(hw);
                double dkJ = xs.DConveyance_DA(hw);
                kSum  += Math.Pow(kJ, 1.5);
                dkSum += 1.5 * Math.Sqrt(Math.Max(kJ, 0.0)) * dkJ;
            }

            double kEq  = Math.Pow(kSum, 2.0 / 3.0);
            double dkEq = kSum > 0 ? (2.0 / 3.0) * Math.Pow(kSum, -1.0 / 3.0) * dkSum : 0.0;
            return Hydraulics.DFrictionSlope_DA(Q, kEq, dkEq);
        }

        /// <summary>
        /// 计算 dSf/dQ，支持多子槽。
        /// 单子槽时回退至基类实现。
        /// </summary>
        public override double DFrictionSlope_DQ(double h, double Q)
        {
            double hw = h + ZMin;
            var subchs = GetSubchannels(hw);
            if (subchs.Length <= 1)
                return base.DFrictionSlope_DQ(h, Q);

            double kSum = 0.0;
            var roughness = GetRoughnessPara();
            foreach (var (xSeg, zSeg) in subchs)
            {
                var xs = new IrregularSection(xSeg, zSeg);
                xs.SetRoughnessPara(roughness);
                double kJ = xs.Conveyance(hw);
                kSum += Math.Pow(kJ, 1.5);
            }
            double kEq = Math.Pow(kSum, 2.0 / 3.0);
            return Hydraulics.DFrictionSlope_DQ(Q, kEq);
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
        /// <para>
        /// 支持：同类型梯形、同类型不规则、以及混合类型（至少一方为不规则断面）的插值。
        /// 权重规则：w1 = dist2/(dist1+dist2)，w2 = dist1/(dist1+dist2)。
        /// </para>
        /// </summary>
        public static CrossSection Interpolate(CrossSection xs1, CrossSection xs2, double dist1, double dist2)
        {
            double totalDist = dist1 + dist2;
            if (totalDist <= 1e-9) return xs1;  // 数值容差：距离极小时直接返回 xs1
            if (dist1     <= 1e-9) return xs1;
            if (dist2     <= 1e-9) return xs2;

            double w1 = dist2 / totalDist;   // xs1 的权重（离 xs1 越近权重越大）
            double w2 = dist1 / totalDist;   // xs2 的权重

            // 插值共享（非几何）属性
            double nL = xs1.NLeft  * w1 + xs2.NLeft  * w2;
            double nM = xs1.NMain  * w1 + xs2.NMain  * w2;
            double nR = xs1.NRight * w1 + xs2.NRight * w2;
            double? bedSlope = (xs1.BedSlope.HasValue && xs2.BedSlope.HasValue)
                ? (double?)(xs1.BedSlope.Value * w1 + xs2.BedSlope.Value * w2)
                : (xs1.BedSlope ?? xs2.BedSlope);
            double curvature = xs1.Curvature * w1 + xs2.Curvature * w2;

            // ── Case 1: 两者均为梯形断面 ──────────────────────────────────────
            if (xs1 is TrapezoidalSection t1 && xs2 is TrapezoidalSection t2)
            {
                double zBed  = t1.ZMin  * w1 + t2.ZMin  * w2;
                double bMain = t1.BMain * w1 + t2.BMain * w2;
                double mMain = t1.MMain * w1 + t2.MMain * w2;

                // 平滩水深（简单断面取 0）
                double yBank1   = t1.IsCompound ? t1.ZBank - t1.ZMin : 0.0;
                double yBank2   = t2.IsCompound ? t2.ZBank - t2.ZMin : 0.0;
                double yBankNew = yBank1 * w1 + yBank2 * w2;
                double? zBankNew = yBankNew > 1e-6 ? (double?)(zBed + yBankNew) : null;

                double bFpL = t1.BFpLeft  * w1 + t2.BFpLeft  * w2;
                double bFpR = t1.BFpRight * w1 + t2.BFpRight * w2;
                double mFp  = t1.MFp      * w1 + t2.MFp      * w2;

                return new TrapezoidalSection(bMain, mMain, zBed, nM, bedSlope, curvature,
                                              zBankNew, bFpL, bFpR, mFp, nL, nR);
            }

            // ── Case 2: 两者均为不规则断面 ────────────────────────────────────
            if (xs1 is IrregularSection ir1 && xs2 is IrregularSection ir2)
            {
                // 取两者 X 坐标的并集作为主控 X
                var xMaster = ir1.X.Union(ir2.X).OrderBy(v => v).ToArray();
                double[] zNew = new double[xMaster.Length];
                for (int i = 0; i < xMaster.Length; i++)
                {
                    double z1 = Hydraulics.Interp(xMaster[i], ir1.X, ir1.Z);
                    double z2 = Hydraulics.Interp(xMaster[i], ir2.X, ir2.Z);
                    zNew[i] = z1 * w1 + z2 * w2;
                }
                var newCs = new IrregularSection(xMaster, zNew, nM, bedSlope, curvature);
                newCs.SetRoughnessPara((nL, nM, nR,
                                       xs1.LeftFpLimit  * w1 + xs2.LeftFpLimit  * w2,
                                       xs1.RightFpLimit * w1 + xs2.RightFpLimit * w2));
                return newCs;
            }

            // ── Case 3: 混合类型（至少一方为不规则断面）────────────────────────
            {
                double[] xMaster;
                if      (xs1 is IrregularSection irA) xMaster = irA.X;
                else if (xs2 is IrregularSection irB) xMaster = irB.X;
                else return xs1; // 不应到达此分支

                double[] zNew = new double[xMaster.Length];
                for (int i = 0; i < xMaster.Length; i++)
                {
                    double z1 = xs1.ZAt(xMaster[i]);
                    double z2 = xs2.ZAt(xMaster[i]);
                    // 处理梯形断面边界外的无穷大（取 ZMin + 100 m 作为安全上限）
                    if (double.IsInfinity(z1) || double.IsNaN(z1)) z1 = xs1.ZMin + 100.0;
                    if (double.IsInfinity(z2) || double.IsNaN(z2)) z2 = xs2.ZMin + 100.0;
                    zNew[i] = z1 * w1 + z2 * w2;
                }
                var newCs = new IrregularSection(xMaster, zNew, nM, bedSlope, curvature);
                newCs.SetRoughnessPara((nL, nM, nR,
                                       xs1.LeftFpLimit  * w1 + xs2.LeftFpLimit  * w2,
                                       xs1.RightFpLimit * w1 + xs2.RightFpLimit * w2));
                return newCs;
            }
        }
    }
}
