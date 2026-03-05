using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroModel
{
    /// <summary>
    /// Represents a channel with hydraulic and geometric attributes.
    /// Equivalent to channel.py.
    /// </summary>
    public class Channel
    {
        public double? InitialConditions_h_at_i(int i) => InitialConditions?[i, 0];
        public double? InitialConditions_Q_at_i(int i) => InitialConditions?[i, 1];

        /// <summary>Initial conditions array: [n_nodes, 2] where column 0 = depth, column 1 = flow.</summary>
        public double[,]? InitialConditions { get; private set; }
        public bool ConditionsInitialized { get; private set; }

        public double InitialFlowRate { get; }
        public double? Roughness { get; }
        public double? Width { get; }

        public double Length { get; }
        public Boundary UpstreamBoundary { get; }
        public Boundary DownstreamBoundary { get; }

        public string InterpolationMethod { get; }

        public double[]? XsChainages { get; private set; }
        public List<CrossSection>? InputXs { get; private set; }
        public double[]? ChAtNode { get; private set; }
        public List<CrossSection>? XsAtNode { get; private set; }

        // Coordinate data for curvature calculation
        private double[]? _coordsChainages;
        private double[,]? _coords;

        public Channel(Boundary upstreamBoundary, Boundary downstreamBoundary,
            double initialFlow, double? roughness = null, double? width = null,
            string interpolationMethod = "GVF_equation")
        {
            InitialConditions = null;
            ConditionsInitialized = false;

            InitialFlowRate = initialFlow;
            Roughness = roughness;
            Width = width;

            Length = downstreamBoundary.Chainage - upstreamBoundary.Chainage;
            UpstreamBoundary = upstreamBoundary;
            DownstreamBoundary = downstreamBoundary;

            if (!new[] { "linear", "GVF_equation", "steady-state" }.Contains(interpolationMethod))
                throw new ArgumentException("Invalid interpolation method.");

            InterpolationMethod = interpolationMethod;
        }

        /// <summary>Computes the energy slope Se at node i.</summary>
        public double Se(double depth, double flow, int i)
        {
            var xs = XsAtNode![i];
            return xs.FrictionSlope(depth, flow) + xs.CurvatureSlope(depth, flow);
        }

        /// <summary>dSe/dA at node i.</summary>
        public double DSeDArea(double depth, double flow, int i)
        {
            var xs = XsAtNode![i];
            return xs.DFrictionSlopeDArea(depth, flow) + xs.DCurvatureSlopeDArea(depth, flow);
        }

        /// <summary>dSe/dQ at node i.</summary>
        public double DSeDFlow(double depth, double flow, int i)
        {
            var xs = XsAtNode![i];
            return xs.DFrictionSlopeDFlow(depth, flow) + xs.DCurvatureSlopeDFlow(depth, flow);
        }

        /// <summary>Computes and stores initial conditions for all nodes.</summary>
        public void InitializeConditions(int nNodes)
        {
            InitializeGeometry(nNodes);

            InitialConditions = new double[nNodes, 2];
            double Q = InitialFlowRate;

            switch (InterpolationMethod)
            {
                case "linear":
                    LinearConditions(nNodes, Q);
                    break;
                case "GVF_equation":
                    GvfConditions(nNodes, Q);
                    break;
                case "steady-state":
                    SteadyConditions(nNodes, Q);
                    break;
                default:
                    throw new ArgumentException("Invalid interpolation method.");
            }

            ConditionsInitialized = true;
        }

        /// <summary>Sets coordinate data along the channel centerline.</summary>
        public void SetCoords(double[,] coords, double[] chainages)
        {
            _coordsChainages = chainages;
            _coords = coords;
        }

        /// <summary>Registers base cross-sections and their chainages.</summary>
        public void SetCrossSections(double[] chainages, List<CrossSection> sections)
        {
            if (chainages.Length != sections.Count)
                throw new ArgumentException("chainages and sections must have same length.");
            for (int i = 0; i < chainages.Length - 1; i++)
                if (chainages[i + 1] <= chainages[i])
                    throw new ArgumentException("chainages must be strictly increasing.");

            XsChainages = chainages;
            InputXs = sections;
        }

        public double AreaAt(int i, double hw) => XsAtNode![i].Area(hw);
        public double HydraulicRadiusAt(int i, double hw) => XsAtNode![i].HydraulicRadius(hw);
        public double TopWidthAt(int i, double hw) => XsAtNode![i].TopWidth(hw);
        public double BedLevelAt(int i) => XsAtNode![i].ZMin;
        public double DAreaDDepthAt(int i, double hw) => XsAtNode![i].DAreaDDepth(hw);

        private void InitializeGeometry(int nNodes)
        {
            if (XsChainages == null || InputXs == null)
                CreateProvisionalCrossSections();

            InterpolateChainages(nNodes);
            InterpolateCrossSections();
        }

        private void InterpolateCrossSections()
        {
            if (_coordsChainages != null && _coords != null)
                CalcCurvature();

            XsAtNode = new List<CrossSection>();
            foreach (double s in ChAtNode!)
            {
                if (s <= XsChainages![0])
                {
                    XsAtNode.Add(InputXs![0]);
                    continue;
                }
                if (s >= XsChainages[XsChainages.Length - 1])
                {
                    XsAtNode.Add(InputXs![InputXs.Count - 1]);
                    continue;
                }

                int j = BinarySearchLower(XsChainages!, s) - 1;
                double chLeft = XsChainages![j];
                double chRight = XsChainages[j + 1];

                var xsInterp = CrossSectionInterpolation.Interpolate(
                    InputXs![j], InputXs[j + 1],
                    s - chLeft, chRight - s);
                XsAtNode.Add(xsInterp);
            }

            UpstreamBoundary.CrossSection = XsAtNode[0];
            DownstreamBoundary.CrossSection = XsAtNode[XsAtNode.Count - 1];
        }

        private void CalcCurvature()
        {
            for (int i = 1; i < InputXs!.Count - 1; i++)
            {
                double chLeft = XsChainages![i - 1];
                double ch = XsChainages[i];
                double chRight = XsChainages[i + 1];

                double[] chs = { chLeft, ch, chRight };
                double[] xCoords = chs.Select(c => Interp1D(c, _coordsChainages!, GetColumn(_coords!, 0))).ToArray();
                double[] yCoords = chs.Select(c => Interp1D(c, _coordsChainages!, GetColumn(_coords!, 1))).ToArray();

                // Direction vectors
                double v1x = xCoords[1] - xCoords[0], v1y = yCoords[1] - yCoords[0];
                double v2x = xCoords[2] - xCoords[1], v2y = yCoords[2] - yCoords[1];

                double norm1 = Math.Sqrt(v1x * v1x + v1y * v1y);
                double norm2 = Math.Sqrt(v2x * v2x + v2y * v2y);

                if (norm1 == 0 || norm2 == 0)
                {
                    InputXs[i].Curvature = 0;
                    continue;
                }

                double dot = (v1x * v2x + v1y * v2y) / (norm1 * norm2);
                double theta = Math.Acos(Math.Max(-1.0, Math.Min(1.0, dot)));
                double L = 0.5 * (norm1 + norm2);
                double cross = v1x * v2y - v1y * v2x;
                InputXs[i].Curvature = 2.0 * Math.Sin(theta / 2.0) / L * Math.Sign(cross);
            }
        }

        private void InterpolateChainages(int nNodes)
        {
            ChAtNode = new double[nNodes];
            for (int i = 0; i < nNodes; i++)
                ChAtNode[i] = UpstreamBoundary.Chainage
                              + (DownstreamBoundary.Chainage - UpstreamBoundary.Chainage) * i / (nNodes - 1);
        }

        private void CreateProvisionalCrossSections()
        {
            var usXs = new TrapezoidalSection(
                zBed: UpstreamBoundary.BedLevel!.Value,
                bMain: Width!.Value,
                mMain: 0,
                nMain: Roughness!.Value);

            var dsXs = new TrapezoidalSection(
                zBed: DownstreamBoundary.BedLevel!.Value,
                bMain: Width.Value,
                mMain: 0,
                nMain: Roughness.Value);

            double bedSlope = (usXs.ZMin - dsXs.ZMin) / Length;
            usXs.BedSlope = bedSlope;
            dsXs.BedSlope = bedSlope;

            UpstreamBoundary.CrossSection = usXs;
            DownstreamBoundary.CrossSection = dsXs;

            XsChainages = new[] { UpstreamBoundary.Chainage, DownstreamBoundary.Chainage };
            InputXs = new List<CrossSection> { usXs, dsXs };
        }

        private void SteadyConditions(int nNodes, double Q)
        {
            for (int i = 0; i < nNodes; i++)
            {
                var xs = XsAtNode![i];
                if (xs.BedSlope == null)
                    throw new InvalidOperationException("Bed slope must be defined.");
                double h = xs.NormalDepth(Q);
                InitialConditions![i, 0] = h;
                InitialConditions[i, 1] = Q;
            }
        }

        private void GvfConditions(int nNodes, double Q)
        {
            double dx = Length / (nNodes - 1);

            double hDown = DownstreamBoundary.InitialDepth!.Value;
            InitialConditions![nNodes - 1, 0] = hDown;
            InitialConditions[nNodes - 1, 1] = Q;

            double GetDhDx(double hIn, int nodeIdx)
            {
                double hw = hIn + BedLevelAt(nodeIdx);
                double A = AreaAt(nodeIdx, hw);
                double T = TopWidthAt(nodeIdx, hw);

                if (T < 1e-6 || A < 1e-6) return 0.0;

                double Fr = Hydraulics.FroudeNumber(T, A, Q);
                if (Fr > 1.0)
                    throw new InvalidOperationException(
                        $"GVF Error: Flow became supercritical (Fr={Fr:F2}) at node {nodeIdx}.");

                double frSq = Fr * Fr;
                double denominator = 1.0 - frSq;
                if (denominator < 0.01)
                {
                    Console.WriteLine($"Warning: GVF approaching critical depth at node {nodeIdx} (Fr={Fr:F2}). Clamping.");
                    denominator = 0.01;
                }

                double s0 = (BedLevelAt(nodeIdx) - BedLevelAt(nodeIdx + 1)) / dx;
                double sf = Se(hIn, Q, nodeIdx);

                return (s0 - sf) / denominator;
            }

            double h = hDown;
            for (int i = nNodes - 2; i >= 0; i--)
            {
                double hDownNode = h;

                // Predictor
                double dhDxDown = GetDhDx(hDownNode, i + 1);
                double hPred = hDownNode - dhDxDown * dx;
                if (hPred <= 0) hPred = 0.01;

                // Corrector
                double dhDxPred = GetDhDx(hPred, i);
                double dhDxAvg = 0.5 * (dhDxDown + dhDxPred);
                double hUp = hDownNode - dhDxAvg * dx;

                if (hUp <= 0)
                {
                    Console.WriteLine($"Warning: GVF h <= 0 at node {i}. Setting to 0.01.");
                    hUp = 0.01;
                }

                h = hUp;
                InitialConditions![i, 0] = h;
                InitialConditions[i, 1] = Q;
            }
        }

        private void LinearConditions(int nNodes, double Q)
        {
            double h0 = UpstreamBoundary.InitialDepth!.Value;
            double hN = DownstreamBoundary.InitialDepth!.Value;

            for (int i = 0; i < nNodes; i++)
            {
                double distance = Length * i / (nNodes - 1);
                double h = h0 + (hN - h0) * distance / Length;
                InitialConditions![i, 0] = h;
                InitialConditions[i, 1] = Q;
            }
        }

        // Helpers
        private static int BinarySearchLower(double[] arr, double val)
        {
            int lo = 0, hi = arr.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (arr[mid] <= val) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static double Interp1D(double x, double[] xp, double[] yp)
            => Utility.Interp(x, xp, yp);

        private static double[] GetColumn(double[,] arr, int col)
        {
            int n = arr.GetLength(0);
            double[] result = new double[n];
            for (int i = 0; i < n; i++)
                result[i] = arr[i, col];
            return result;
        }
    }
}
