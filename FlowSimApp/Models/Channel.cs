using System;
using System.Linq;
using System.Collections.Generic;

namespace FlowSim.Models
{
    public enum InitializationMethod { Linear, GVFEquation, SteadyState }

    /// <summary>Represents a 1D open channel.</summary>
    public class Channel
    {
        public Boundary UpstreamBoundary { get; }
        public Boundary DownstreamBoundary { get; }
        public double InitialFlowRate { get; }
        public double? Roughness { get; }
        public double? Width { get; }
        public double Length { get; }
        public InitializationMethod InitMethod { get; }
        public double[,]? InitialConditions { get; private set; }
        public bool ConditionsInitialized { get; private set; }
        public CrossSection[]? XsAtNode { get; private set; }
        public double[]? ChAtNode { get; private set; }

        private double[]? _xsChainages;
        private CrossSection[]? _inputXs;

        public Channel(Boundary upstreamBoundary, Boundary downstreamBoundary,
                       double initialFlow, double? roughness = null, double? width = null,
                       InitializationMethod initMethod = InitializationMethod.GVFEquation)
        {
            UpstreamBoundary = upstreamBoundary;
            DownstreamBoundary = downstreamBoundary;
            InitialFlowRate = initialFlow;
            Roughness = roughness;
            Width = width;
            Length = downstreamBoundary.Chainage - upstreamBoundary.Chainage;
            InitMethod = initMethod;
        }

        public void SetCrossSection(double[] chainages, CrossSection[] sections)
        {
            if (chainages.Length != sections.Length)
                throw new ArgumentException("Chainages and sections must have same length.");
            _xsChainages = chainages;
            _inputXs = sections;
        }

        public void InitializeConditions(int nNodes)
        {
            _initializeGeometry(nNodes);
            InitialConditions = new double[nNodes, 2];
            double Q = InitialFlowRate;

            switch (InitMethod)
            {
                case InitializationMethod.Linear:
                    _linearConditions(nNodes, Q);
                    break;
                case InitializationMethod.GVFEquation:
                    _gvfConditions(nNodes, Q);
                    break;
                case InitializationMethod.SteadyState:
                    _steadyConditions(nNodes, Q);
                    break;
            }
            ConditionsInitialized = true;
        }

        public double Se(double h, double Q, int i)
        {
            var xs = XsAtNode![i];
            return xs.FrictionSlope(h, Q) + xs.CurvatureSlope(h, Q);
        }

        public double DSe_DA(double h, double Q, int i)
        {
            var xs = XsAtNode![i];
            return xs.DFrictionSlope_DA(h, Q) + xs.DCurvatureSlope_DA(h, Q);
        }

        public double DSe_DQ(double h, double Q, int i)
        {
            var xs = XsAtNode![i];
            return xs.DFrictionSlope_DQ(h, Q) + xs.DCurvatureSlope_DQ(h, Q);
        }

        public double AreaAt(int i, double hw) => XsAtNode![i].Area(hw);
        public double HydraulicRadius(int i, double hw) => XsAtNode![i].HydraulicRadius(hw);
        public double TopWidth(int i, double hw) => XsAtNode![i].TopWidth(hw);
        public double BedLevelAt(int i) => XsAtNode![i].ZMin;
        public double DArea_Dh(int i, double hw) => XsAtNode![i].DArea_Dh(hw);

        private void _initializeGeometry(int nNodes)
        {
            if (_xsChainages == null || _inputXs == null)
                _createProvisionalCrossSections();

            ChAtNode = Linspace(UpstreamBoundary.Chainage, DownstreamBoundary.Chainage, nNodes);
            _interpolateCrossSections();
        }

        private void _createProvisionalCrossSections()
        {
            double usBedLevel = UpstreamBoundary.BedLevel ?? 0;
            double dsBedLevel = DownstreamBoundary.BedLevel ?? 0;
            double bedSlope = Length > 0 ? (usBedLevel - dsBedLevel) / Length : 0;

            var usXs = new TrapezoidalSection(Width ?? 100, 0, usBedLevel, Roughness ?? 0.03, bedSlope);
            var dsXs = new TrapezoidalSection(Width ?? 100, 0, dsBedLevel, Roughness ?? 0.03, bedSlope);

            UpstreamBoundary.CrossSection = usXs;
            DownstreamBoundary.CrossSection = dsXs;

            _xsChainages = new[] { UpstreamBoundary.Chainage, DownstreamBoundary.Chainage };
            _inputXs = new CrossSection[] { usXs, dsXs };
        }

        private void _interpolateCrossSections()
        {
            XsAtNode = new CrossSection[ChAtNode!.Length];
            for (int i = 0; i < ChAtNode.Length; i++)
            {
                double s = ChAtNode[i];
                if (s <= _xsChainages![0]) { XsAtNode[i] = _inputXs![0]; continue; }
                if (s >= _xsChainages![_xsChainages.Length - 1]) { XsAtNode[i] = _inputXs![_inputXs.Length - 1]; continue; }

                int j = Array.BinarySearch(_xsChainages, s);
                if (j < 0) j = ~j - 1;

                double chLeft = _xsChainages[j];
                double chRight = _xsChainages[j + 1];
                XsAtNode[i] = CrossSectionInterpolator.Interpolate(
                    _inputXs![j], _inputXs![j + 1],
                    s - chLeft, chRight - s);
            }

            UpstreamBoundary.CrossSection = XsAtNode[0];
            DownstreamBoundary.CrossSection = XsAtNode[XsAtNode.Length - 1];
        }

        private void _steadyConditions(int nNodes, double Q)
        {
            for (int i = 0; i < nNodes; i++)
            {
                var xs = XsAtNode![i];
                if (!xs.BedSlope.HasValue) throw new InvalidOperationException("Bed slope must be defined.");
                double h = xs.NormalDepth(Q);
                InitialConditions![i, 0] = h;
                InitialConditions![i, 1] = Q;
            }
        }

        private void _gvfConditions(int nNodes, double Q)
        {
            double dx = nNodes > 1 ? Length / (nNodes - 1) : Length;
            double h = DownstreamBoundary.InitialDepth ?? 1.0;
            InitialConditions![nNodes - 1, 0] = h;
            InitialConditions![nNodes - 1, 1] = Q;

            double GetDhDx(double hIn, int nodeIdx)
            {
                double hw = hIn + BedLevelAt(nodeIdx);
                double A = AreaAt(nodeIdx, hw);
                double T = TopWidth(nodeIdx, hw);
                if (T < 1e-6 || A < 1e-6) return 0.0;
                double Fr = Hydraulics.FroudeNumber(T, A, Q);
                if (Fr >= 1.0) return 0.0; // avoid supercritical issues in init
                double FrSq = Fr * Fr;
                double denom = Math.Max(1.0 - FrSq, 0.01);
                double S0 = nodeIdx + 1 < nNodes ? (BedLevelAt(nodeIdx) - BedLevelAt(nodeIdx + 1)) / dx : 0;
                double Sf = Se(hIn, Q, nodeIdx);
                return (S0 - Sf) / denom;
            }

            for (int i = nNodes - 2; i >= 0; i--)
            {
                double hDown = h;
                double dhdx_down = GetDhDx(hDown, i + 1);
                double hPred = hDown - dhdx_down * dx;
                if (hPred <= 0) hPred = 0.01;
                double dhdx_pred = GetDhDx(hPred, i);
                double dhdx_avg = 0.5 * (dhdx_down + dhdx_pred);
                double hUp = hDown - dhdx_avg * dx;
                if (hUp <= 0) hUp = 0.01;
                h = hUp;
                InitialConditions![i, 0] = h;
                InitialConditions![i, 1] = Q;
            }
        }

        private void _linearConditions(int nNodes, double Q)
        {
            double h0 = UpstreamBoundary.InitialDepth ?? 1.0;
            double hN = DownstreamBoundary.InitialDepth ?? 1.0;
            for (int i = 0; i < nNodes; i++)
            {
                double h = h0 + (hN - h0) * i / (nNodes - 1);
                InitialConditions![i, 0] = h;
                InitialConditions![i, 1] = Q;
            }
        }

        public static double[] Linspace(double start, double end, int n)
        {
            double[] arr = new double[n];
            for (int i = 0; i < n; i++)
                arr[i] = start + (end - start) * i / (n - 1);
            return arr;
        }
    }
}
