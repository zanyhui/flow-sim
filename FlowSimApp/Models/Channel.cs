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

            // 生成均匀分布的节点桩号
            ChAtNode = Linspace(UpstreamBoundary.Chainage, DownstreamBoundary.Chainage, nNodes);

            // 对每个节点桩号进行断面插值
            _interpolateCrossSections();
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
