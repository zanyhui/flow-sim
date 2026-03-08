using System;
using System.Linq;
using System.Collections.Generic;

namespace FlowSim.Models
{
    /// <summary>
    /// 初始化方法枚举，用于确定仿真开始时河道各节点的水深和流量初始值。
    /// <list type="bullet">
    ///   <item><term>Linear</term><description>在上下游初始水深之间线性插值。</description></item>
    ///   <item><term>GVFEquation</term><description>用渐变流（GVF）方程从下游向上游积分，求稳态水面线。</description></item>
    ///   <item><term>SteadyState</term><description>在每个节点求解正常水深（均匀流）。</description></item>
    /// </list>
    /// </summary>
    public enum InitializationMethod { Linear, GVFEquation, SteadyState }

    /// <summary>
    /// 一维明渠河道模型。
    /// <para>
    /// 存储河道的上下游边界条件、断面信息、初始条件，
    /// 并提供各节点的水力参数查询接口（面积、水力半径、水面宽等）。
    /// 仿真开始前须调用 <see cref="InitializeConditions"/> 完成初始化。
    /// </para>
    /// <para>
    /// 断面可通过 <see cref="SetCrossSection"/> 按桩号指定，
    /// 未指定时自动生成均匀梯形断面（以上下游边界参数为准）。
    /// 中间节点断面通过距离加权插值 (<see cref="CrossSectionInterpolator"/>) 获得。
    /// </para>
    /// </summary>
    public class Channel
    {
        /// <summary>上游边界条件（包含类型、过程线等信息）。</summary>
        public Boundary UpstreamBoundary { get; }

        /// <summary>下游边界条件。</summary>
        public Boundary DownstreamBoundary { get; }

        /// <summary>初始流量（m³/s），用于初始化和渐变流积分。</summary>
        public double InitialFlowRate { get; }

        /// <summary>统一曼宁糙率系数（可选，不指定断面时使用）。</summary>
        public double? Roughness { get; }

        /// <summary>河道宽度（m，可选，不指定断面时使用）。</summary>
        public double? Width { get; }

        /// <summary>河道总长度（m）= 下游桩号 - 上游桩号。</summary>
        public double Length { get; }

        /// <summary>初始化方法（线性/渐变流/正常水深）。</summary>
        public InitializationMethod InitMethod { get; }

        /// <summary>初始条件数组 [nNodes, 2]，第 0 列为水深（m），第 1 列为流量（m³/s）。</summary>
        public double[,]? InitialConditions { get; private set; }

        /// <summary>初始条件是否已完成计算。</summary>
        public bool ConditionsInitialized { get; private set; }

        /// <summary>各计算节点对应的水力断面数组（长度 = NumberOfNodes）。</summary>
        public CrossSection[]? XsAtNode { get; private set; }

        /// <summary>各计算节点的桩号数组（m，从上游到下游均匀分布）。</summary>
        public double[]? ChAtNode { get; private set; }

        // 用户输入的断面桩号和断面实例（可选，不指定时自动生成）
        private double[]? _xsChainages;
        private CrossSection[]? _inputXs;

        // 河道水平坐标（用于弯道曲率计算）
        private double[,]? _coords;          // [n, 2]：每行 (x, y) 坐标（m）
        private double[]? _coordsChainages;  // 对应坐标点的桩号（m）

        /// <summary>
        /// 构造河道对象，设置上下游边界和基本参数。
        /// </summary>
        /// <param name="upstreamBoundary">上游边界条件。</param>
        /// <param name="downstreamBoundary">下游边界条件。</param>
        /// <param name="initialFlow">初始流量（m³/s）。</param>
        /// <param name="roughness">曼宁糙率（可选）。</param>
        /// <param name="width">河道宽度（m，可选）。</param>
        /// <param name="initMethod">初始化方法，默认 GVFEquation。</param>
        public Channel(Boundary upstreamBoundary, Boundary downstreamBoundary,
                       double initialFlow, double? roughness = null, double? width = null,
                       InitializationMethod initMethod = InitializationMethod.GVFEquation)
        {
            UpstreamBoundary = upstreamBoundary;
            DownstreamBoundary = downstreamBoundary;
            InitialFlowRate = initialFlow;
            Roughness = roughness;
            Width = width;
            // 河道长度由下游桩号减去上游桩号得到
            Length = downstreamBoundary.Chainage - upstreamBoundary.Chainage;
            InitMethod = initMethod;
        }

        /// <summary>
        /// 设置用户自定义断面（按桩号指定），供插值使用。
        /// </summary>
        /// <param name="chainages">断面桩号数组（m），须与 sections 等长。</param>
        /// <param name="sections">对应断面实例数组。</param>
        public void SetCrossSection(double[] chainages, CrossSection[] sections)
        {
            if (chainages.Length != sections.Length)
                throw new ArgumentException("Chainages and sections must have same length.");
            _xsChainages = chainages;
            _inputXs = sections;
        }

        /// <summary>
        /// 设置河道水平坐标线，用于计算断面处的弯道曲率。
        /// 与 Python <c>channel.set_coords(coords, chainages)</c> 对应。
        /// 坐标设置后，下次 <see cref="InitializeConditions"/> 时将自动为各输入断面赋予曲率值。
        /// </summary>
        /// <param name="coords">河道中心线坐标数组 [n, 2]，每行为 (x, y)（m）。</param>
        /// <param name="chainages">对应坐标点的桩号数组（m），长度须与 coords 行数相等。</param>
        public void SetCoords(double[,] coords, double[] chainages)
        {
            if (coords.GetLength(0) != chainages.Length)
                throw new ArgumentException("coords rows and chainages must have same length.");
            _coords = coords;
            _coordsChainages = chainages;
        }

        /// <summary>
        /// 初始化河道几何及初始水动力条件。
        /// <para>
        /// 步骤：
        /// 1. 生成均匀分布的节点桩号并为各节点插值断面（<see cref="_initializeGeometry"/>）；
        /// 2. 根据 <see cref="InitMethod"/> 计算各节点初始水深和流量。
        /// </para>
        /// </summary>
        /// <param name="nNodes">计算节点总数。</param>
        public void InitializeConditions(int nNodes)
        {
            _initializeGeometry(nNodes);                  // 步骤 1：建立断面插值网格
            InitialConditions = new double[nNodes, 2];   // 分配初始条件数组
            double Q = InitialFlowRate;

            switch (InitMethod)
            {
                case InitializationMethod.Linear:
                    _linearConditions(nNodes, Q);   // 上下游线性插值
                    break;
                case InitializationMethod.GVFEquation:
                    _gvfConditions(nNodes, Q);       // 渐变流方程积分
                    break;
                case InitializationMethod.SteadyState:
                    _steadyConditions(nNodes, Q);    // 各节点正常水深
                    break;
            }
            ConditionsInitialized = true;
        }

        /// <summary>
        /// 计算节点 i 处的等效能量坡度 Se = Sf + Sc（摩阻坡度 + 弯曲坡度）。
        /// 该项出现在圣维南动量方程中作为阻力项。
        /// </summary>
        /// <param name="h">水深（m）。</param>
        /// <param name="Q">流量（m³/s）。</param>
        /// <param name="i">节点索引。</param>
        /// <returns>等效能量坡度 Se（无量纲）。</returns>
        public double Se(double h, double Q, int i)
        {
            var xs = XsAtNode![i];
            return xs.FrictionSlope(h, Q) + xs.CurvatureSlope(h, Q);
        }

        /// <summary>
        /// 计算 dSe/dA（等效能量坡度对过水面积的偏导），用于 Preissmann 雅可比矩阵。
        /// </summary>
        public double DSe_DA(double h, double Q, int i)
        {
            var xs = XsAtNode![i];
            return xs.DFrictionSlope_DA(h, Q) + xs.DCurvatureSlope_DA(h, Q);
        }

        /// <summary>
        /// 计算 dSe/dQ（等效能量坡度对流量的偏导），用于 Preissmann 雅可比矩阵。
        /// </summary>
        public double DSe_DQ(double h, double Q, int i)
        {
            var xs = XsAtNode![i];
            return xs.DFrictionSlope_DQ(h, Q) + xs.DCurvatureSlope_DQ(h, Q);
        }

        // 以下为各节点水力参数的便捷查询方法（委托给对应节点断面）
        /// <summary>节点 i 处给定水位 hw 的过水面积（m²）。</summary>
        public double AreaAt(int i, double hw) => XsAtNode![i].Area(hw);
        /// <summary>节点 i 处给定水位 hw 的水力半径（m）。</summary>
        public double HydraulicRadius(int i, double hw) => XsAtNode![i].HydraulicRadius(hw);
        /// <summary>节点 i 处给定水位 hw 的水面宽（m）。</summary>
        public double TopWidth(int i, double hw) => XsAtNode![i].TopWidth(hw);
        /// <summary>节点 i 处的床底高程（m）。</summary>
        public double BedLevelAt(int i) => XsAtNode![i].ZMin;
        /// <summary>节点 i 处给定水位 hw 的 dA/dh（水面宽，用于雅可比矩阵）。</summary>
        public double DArea_Dh(int i, double hw) => XsAtNode![i].DArea_Dh(hw);

        /// <summary>
        /// 初始化几何：生成节点桩号并为各节点分配插值断面。
        /// </summary>
        private void _initializeGeometry(int nNodes)
        {
            // 如果未提供用户断面，则自动从上下游边界参数生成临时梯形断面
            if (_xsChainages == null || _inputXs == null)
                _createProvisionalCrossSections();

            // 若已设置水平坐标，则为各输入断面计算弯道曲率
            if (_coords != null && _coordsChainages != null)
                _calcCurvature();

            // 为缺少 BedSlope 的输入断面（如从 CSV 加载的不规则断面）补充河床纵坡。
            // 必须在插值之前执行，使插值断面也能继承正确的坡度值。
            _assignInputBedSlopes();

            // 生成均匀分布的节点桩号
            ChAtNode = Linspace(UpstreamBoundary.Chainage, DownstreamBoundary.Chainage, nNodes);

            // 对每个节点桩号进行断面插值
            _interpolateCrossSections();
        }

        /// <summary>
        /// 为所有 <see cref="BedSlope"/> 尚未赋值的输入断面计算并填充河床纵坡。
        /// <para>
        /// 坡度由相邻断面的 <see cref="CrossSection.ZMin"/>（床底最低点高程）之差
        /// 除以桩号间距得到。对末尾断面取前向差分；对首断面外的其他断面取后向差分。
        /// 计算结果取非负值（上坡或平坡均视为 0），与曼宁公式对正常流的要求一致。
        /// </para>
        /// <para>
        /// 此方法解决了不规则断面在正常水深（NormalDepth）下游边界条件下的数值故障：
        /// 当 <see cref="BedSlope"/> 为 null 时，<see cref="Boundary.ConditionResidual"/>
        /// 会以 S₀ = 0 代入，导致正常流量 = 0，进而使 Lax 格式下游流量始终为 0、
        /// Preissmann 格式下游残差发散。
        /// </para>
        /// </summary>
        private void _assignInputBedSlopes()
        {
            if (_inputXs == null || _xsChainages == null || _inputXs.Length < 2) return;

            for (int i = 0; i < _inputXs.Length; i++)
            {
                if (_inputXs[i].BedSlope.HasValue) continue;   // 已有坡度，跳过

                double dz, dx;
                if (i < _inputXs.Length - 1)
                {
                    // 正向差分：从断面 i 到断面 i+1 的高程降落（下坡为正）
                    dz = _inputXs[i].ZMin - _inputXs[i + 1].ZMin;
                    dx = _xsChainages[i + 1] - _xsChainages[i];
                }
                else
                {
                    // 末尾断面：沿用前向差分（与倒数第二段一致）
                    dz = _inputXs[i - 1].ZMin - _inputXs[i].ZMin;
                    dx = _xsChainages[i] - _xsChainages[i - 1];
                }

                // 坡度不能为负（逆坡不支持正常流）；dx < 1e-9 表示相邻断面桩号几乎重合，视为 0 坡度
                _inputXs[i].BedSlope = dx > 1e-9 ? Math.Max(0.0, dz / dx) : 0.0;
            }
        }

        /// <summary>
        /// 根据上下游边界参数（床底高程、宽度、糙率）自动创建两个端部梯形断面，
        /// 供后续插值使用。床底坡度由高程差 / 河道长度计算。
        /// </summary>
        private void _createProvisionalCrossSections()
        {
            double usBedLevel = UpstreamBoundary.BedLevel ?? 0;
            double dsBedLevel = DownstreamBoundary.BedLevel ?? 0;
            double bedSlope = Length > 0 ? (usBedLevel - dsBedLevel) / Length : 0;   // 河床纵坡

            // 上游梯形断面（矩形，边坡系数 = 0）
            var usXs = new TrapezoidalSection(Width ?? 100, 0, usBedLevel, Roughness ?? 0.03, bedSlope);
            // 下游梯形断面
            var dsXs = new TrapezoidalSection(Width ?? 100, 0, dsBedLevel, Roughness ?? 0.03, bedSlope);

            // 将断面注入边界对象（后续边界残差计算时需要）
            UpstreamBoundary.CrossSection = usXs;
            DownstreamBoundary.CrossSection = dsXs;

            _xsChainages = new[] { UpstreamBoundary.Chainage, DownstreamBoundary.Chainage };
            _inputXs = new CrossSection[] { usXs, dsXs };
        }

        /// <summary>
        /// 根据河道水平坐标（通过 <see cref="SetCoords"/> 设置）计算各输入断面处的弯道曲率，
        /// 并将曲率值赋予对应 <see cref="CrossSection.Curvature"/> 属性。
        /// <para>
        /// 与 Python <c>channel._calc_curvature()</c> 对应。
        /// 算法：对每个中间断面（非端部），取左中右三个桩号对应的平面坐标，
        /// 用转向角 θ 和平均弦长 L 估算曲率 κ = 2·sin(θ/2)/L，
        /// 符号由右手叉积决定（左转为正，右转为负）。
        /// </para>
        /// </summary>
        private void _calcCurvature()
        {
            if (_inputXs == null || _xsChainages == null || _coords == null || _coordsChainages == null)
                return;

            int nCoords = _coordsChainages.Length;
            double[] cxs = new double[nCoords];   // 坐标 x 列
            double[] cys = new double[nCoords];   // 坐标 y 列
            for (int k = 0; k < nCoords; k++)
            {
                cxs[k] = _coords[k, 0];
                cys[k] = _coords[k, 1];
            }

            // 对每个中间断面（跳过首尾端部）计算曲率
            for (int i = 1; i < _inputXs.Length - 1; i++)
            {
                double chLeft  = _xsChainages[i - 1];
                double chMid   = _xsChainages[i];
                double chRight = _xsChainages[i + 1];

                // 从坐标线上插值得到三点坐标
                double xL = Hydraulics.Interp(chLeft,  _coordsChainages, cxs);
                double yL = Hydraulics.Interp(chLeft,  _coordsChainages, cys);
                double xM = Hydraulics.Interp(chMid,   _coordsChainages, cxs);
                double yM = Hydraulics.Interp(chMid,   _coordsChainages, cys);
                double xR = Hydraulics.Interp(chRight, _coordsChainages, cxs);
                double yR = Hydraulics.Interp(chRight, _coordsChainages, cys);

                // 方向向量 v1 = M-L，v2 = R-M
                double v1x = xM - xL, v1y = yM - yL;
                double v2x = xR - xM, v2y = yR - yM;
                double len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                double len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

                if (len1 < 1e-12 || len2 < 1e-12) { _inputXs[i].Curvature = 0.0; continue; }

                // 余弦 → 转向角 θ，叉积决定符号
                double dot   = v1x * v2x + v1y * v2y;
                double cosTheta = Math.Max(-1.0, Math.Min(1.0, dot / (len1 * len2)));
                double theta = Math.Acos(cosTheta);
                double cross = v1x * v2y - v1y * v2x;   // 2D 叉积（z 分量）

                // 平均弦长，κ = 2·sin(θ/2) / L
                double L = 0.5 * (len1 + len2);
                double curvature = 2.0 * Math.Sin(theta / 2.0) / L * Math.Sign(cross);
                _inputXs[i].Curvature = curvature;
            }
        }

        /// <summary>
        /// 对各计算节点进行断面插值：
        /// 1. 对每个节点桩号，在 _xsChainages 中二分查找所在区间；
        /// 2. 用距离权重插值（<see cref="CrossSectionInterpolator"/>）生成节点断面；
        /// 3. 将上下游端部断面同步注入边界对象。
        /// </summary>
        private void _interpolateCrossSections()
        {
            XsAtNode = new CrossSection[ChAtNode!.Length];
            for (int i = 0; i < ChAtNode.Length; i++)
            {
                double s = ChAtNode[i];   // 当前节点桩号

                // 边界情况：节点位于或超出输入断面范围，直接用端部断面
                if (s <= _xsChainages![0]) { XsAtNode[i] = _inputXs![0]; continue; }
                if (s >= _xsChainages![_xsChainages.Length - 1]) { XsAtNode[i] = _inputXs![_inputXs.Length - 1]; continue; }

                // 二分查找节点所在的插值区间 [j, j+1]
                int j = Array.BinarySearch(_xsChainages, s);
                if (j < 0) j = ~j - 1;   // j = 插入位置 - 1

                double chLeft  = _xsChainages[j];
                double chRight = _xsChainages[j + 1];
                // 距离权重插值：dist1 = s - chLeft，dist2 = chRight - s
                XsAtNode[i] = CrossSectionInterpolator.Interpolate(
                    _inputXs![j], _inputXs![j + 1],
                    s - chLeft, chRight - s);
            }

            // 更新边界对象的断面引用（始终取端部节点断面）
            UpstreamBoundary.CrossSection   = XsAtNode[0];
            DownstreamBoundary.CrossSection = XsAtNode[XsAtNode.Length - 1];
        }

        /// <summary>
        /// 以正常水深作为各节点初始水深（SteadyState 方法）。
        /// 在每个节点处调用 Brent 方法求解正常水深（需要断面有坡度信息）。
        /// </summary>
        private void _steadyConditions(int nNodes, double Q)
        {
            for (int i = 0; i < nNodes; i++)
            {
                var xs = XsAtNode![i];
                if (!xs.BedSlope.HasValue) throw new InvalidOperationException("Bed slope must be defined.");
                double h = xs.NormalDepth(Q);           // 求解对应坡度和流量下的正常水深
                InitialConditions![i, 0] = h;
                InitialConditions![i, 1] = Q;
            }
        }

        /// <summary>
        /// 用渐变流（GVF）方程求初始水面线（GVFEquation 方法）。
        /// <para>
        /// 算法：从下游向上游逐节点积分。
        /// 水面线方程：dh/dx = (S0 - Sf) / (1 - Fr²)
        /// 其中 S0 = 河床坡度，Sf = 摩阻坡度，Fr = 弗劳德数。
        /// 积分采用预测-修正（Heun 法，二阶精度）：
        /// 1. 预测值：hPred = hDown - (dh/dx)_down * dx；
        /// 2. 修正值：hUp = hDown - 0.5*(dhdx_down + dhdx_pred) * dx。
        /// 当出现超临界流（Fr≥1）时，梯度设为 0（简化处理）。
        /// </para>
        /// </summary>
        private void _gvfConditions(int nNodes, double Q)
        {
            double dx = nNodes > 1 ? Length / (nNodes - 1) : Length;  // 空间步长
            double h = DownstreamBoundary.InitialDepth ?? 1.0;         // 从下游已知水深开始

            // 设置下游末端节点初始条件
            InitialConditions![nNodes - 1, 0] = h;
            InitialConditions![nNodes - 1, 1] = Q;

            // 定义局部函数：计算节点 nodeIdx 处的水面梯度 dh/dx
            double GetDhDx(double hIn, int nodeIdx)
            {
                double hw = hIn + BedLevelAt(nodeIdx);       // 绝对水位
                double A = AreaAt(nodeIdx, hw);
                double T = TopWidth(nodeIdx, hw);
                if (T < 1e-6 || A < 1e-6) return 0.0;        // 避免极小断面引起除零

                double Fr = Hydraulics.FroudeNumber(T, A, Q);
                if (Fr >= 1.0) return 0.0;                    // 超临界流时不用 GVF 方程
                double FrSq = Fr * Fr;
                double denom = Math.Max(1.0 - FrSq, 0.01);   // 分母下限防除零

                // 局部河床坡度 S0（由相邻节点床底高程差计算）
                double S0 = nodeIdx + 1 < nNodes ? (BedLevelAt(nodeIdx) - BedLevelAt(nodeIdx + 1)) / dx : 0;
                double Sf = Se(hIn, Q, nodeIdx);               // 等效能量坡度（摩阻+弯曲）
                return (S0 - Sf) / denom;                      // GVF 水面梯度公式
            }

            // 从下游向上游逐节点用 Heun 法积分
            for (int i = nNodes - 2; i >= 0; i--)
            {
                double hDown    = h;
                double dhdx_down = GetDhDx(hDown, i + 1);       // 下游节点梯度
                double hPred    = hDown - dhdx_down * dx;        // 预测值（Euler 步）
                if (hPred <= 0) hPred = 0.01;                    // 防止水深为负

                double dhdx_pred = GetDhDx(hPred, i);           // 预测点梯度
                double dhdx_avg  = 0.5 * (dhdx_down + dhdx_pred); // 平均梯度（Heun 修正）
                double hUp = hDown - dhdx_avg * dx;              // 修正值
                if (hUp <= 0) hUp = 0.01;

                h = hUp;
                InitialConditions![i, 0] = h;
                InitialConditions![i, 1] = Q;
            }
        }

        /// <summary>
        /// 上下游水深线性插值初始化（Linear 方法）。
        /// 最简单的初始化方式，计算量极小。
        /// </summary>
        private void _linearConditions(int nNodes, double Q)
        {
            double h0 = UpstreamBoundary.InitialDepth ?? 1.0;    // 上游初始水深
            double hN = DownstreamBoundary.InitialDepth ?? 1.0;  // 下游初始水深
            for (int i = 0; i < nNodes; i++)
            {
                // 线性插值：h_i = h0 + (hN - h0) * i / (nNodes - 1)
                double h = h0 + (hN - h0) * i / (nNodes - 1);
                InitialConditions![i, 0] = h;
                InitialConditions![i, 1] = Q;
            }
        }

        /// <summary>
        /// 生成 [start, end] 之间均匀分布的 n 个点（类似 NumPy linspace）。
        /// 返回数组中 arr[0]=start，arr[n-1]=end。
        /// </summary>
        /// <param name="start">起始值。</param>
        /// <param name="end">终止值。</param>
        /// <param name="n">点数（含端点）。</param>
        /// <returns>均匀分布的数组。</returns>
        public static double[] Linspace(double start, double end, int n)
        {
            double[] arr = new double[n];
            for (int i = 0; i < n; i++)
                arr[i] = start + (end - start) * i / (n - 1);
            return arr;
        }
    }
}
