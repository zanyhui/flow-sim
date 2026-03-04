using System;

namespace FlowSim.Models
{
    public enum LaxSecondaryBC { Constant, Mirror, Linear }

    /// <summary>Lax-Friedrichs explicit finite-difference scheme for Saint-Venant equations.</summary>
    public class LaxSolver : Solver
    {
        public LaxSecondaryBC UpstreamSecondaryBC { get; }
        public LaxSecondaryBC DownstreamSecondaryBC { get; }

        public LaxSolver(Channel channel, double timeStep, double spatialStep, double simulationTime,
                         LaxSecondaryBC upstreamSecondaryBC = LaxSecondaryBC.Constant,
                         LaxSecondaryBC downstreamSecondaryBC = LaxSecondaryBC.Constant,
                         bool fitSpatialStep = true)
            : base(channel, timeStep, spatialStep, simulationTime, fitSpatialStep)
        {
            UpstreamSecondaryBC = upstreamSecondaryBC;
            DownstreamSecondaryBC = downstreamSecondaryBC;
            InitializeT0();
        }

        public override void Run(int verbose = 1)
        {
            bool running = true;
            while (running)
            {
                TimeLevel++;
                if (TimeLevel >= NumberOfTimeLevels)
                {
                    TimeLevel = NumberOfTimeLevels - 1;
                    running = false;
                    break;
                }

                if (verbose >= 1) Console.WriteLine($"\n> Time level #{TimeLevel}");

                for (int i = 0; i < NumberOfNodes; i++)
                    ComputeNode(i);

                CheckCflAll();
            }

            base.Finalize(verbose);
        }

        private (double A, double Q, double Y, double Se) UsGhostNode()
        {
            switch (UpstreamSecondaryBC)
            {
                case LaxSecondaryBC.Mirror:
                    return (AreaAt(TimeLevel - 1, 1), FlowAt(TimeLevel - 1, 1),
                            WaterLevelAt(TimeLevel - 1, 1), SeAt(TimeLevel - 1, 1));
                case LaxSecondaryBC.Linear:
                    return (2 * AreaAt(TimeLevel - 1, 0) - AreaAt(TimeLevel - 1, 1),
                            2 * FlowAt(TimeLevel - 1, 0) - FlowAt(TimeLevel - 1, 1),
                            2 * WaterLevelAt(TimeLevel - 1, 0) - WaterLevelAt(TimeLevel - 1, 1),
                            2 * SeAt(TimeLevel - 1, 0) - SeAt(TimeLevel - 1, 1));
                default: // Constant
                    return (AreaAt(TimeLevel - 1, 0), FlowAt(TimeLevel - 1, 0),
                            WaterLevelAt(TimeLevel - 1, 0), SeAt(TimeLevel - 1, 0));
            }
        }

        private (double A, double Q, double Y, double Se) DsGhostNode()
        {
            int last = NumberOfNodes - 1;
            switch (DownstreamSecondaryBC)
            {
                case LaxSecondaryBC.Mirror:
                    return (AreaAt(TimeLevel - 1, last - 1), FlowAt(TimeLevel - 1, last - 1),
                            WaterLevelAt(TimeLevel - 1, last - 1), SeAt(TimeLevel - 1, last - 1));
                case LaxSecondaryBC.Linear:
                    return (2 * AreaAt(TimeLevel - 1, last) - AreaAt(TimeLevel - 1, last - 1),
                            2 * FlowAt(TimeLevel - 1, last) - FlowAt(TimeLevel - 1, last - 1),
                            2 * WaterLevelAt(TimeLevel - 1, last) - WaterLevelAt(TimeLevel - 1, last - 1),
                            2 * SeAt(TimeLevel - 1, last) - SeAt(TimeLevel - 1, last - 1));
                default: // Constant
                    return (AreaAt(TimeLevel - 1, last), FlowAt(TimeLevel - 1, last),
                            WaterLevelAt(TimeLevel - 1, last), SeAt(TimeLevel - 1, last));
            }
        }

        private void ComputeNode(int i)
        {
            if (i == 0)
                ComputeUpstreamNode();
            else if (i == NumberOfNodes - 1)
                ComputeDownstreamNode();
            else
            {
                double A_im1 = AreaAt(TimeLevel - 1, i - 1);
                double A_ip1 = AreaAt(TimeLevel - 1, i + 1);
                double Q_im1 = FlowAt(TimeLevel - 1, i - 1);
                double Q_ip1 = FlowAt(TimeLevel - 1, i + 1);
                double Y_im1 = WaterLevelAt(TimeLevel - 1, i - 1);
                double Y_ip1 = WaterLevelAt(TimeLevel - 1, i + 1);
                double Se_im1 = SeAt(TimeLevel - 1, i - 1);
                double Se_ip1 = SeAt(TimeLevel - 1, i + 1);

                double newA = NewArea(A_im1, A_ip1, Q_im1, Q_ip1);
                double newQ = NewFlow(A_im1, A_ip1, Q_im1, Q_ip1, Y_im1, Y_ip1, Se_im1, Se_ip1);

                // Convert area back to depth
                Depth![TimeLevel, i] = AreaToDepth(i, newA);
                Flow![TimeLevel, i] = newQ;
            }
        }

        private void ComputeUpstreamNode()
        {
            var (ghostA, ghostQ, ghostY, ghostSe) = UsGhostNode();

            // If upstream BC is flow-dependent (FlowHydrograph, NormalDepth, RatingCurve), compute A from Lax, set Q from BC
            if (Channel.UpstreamBoundary.IsFlowDependent)
            {
                double newA = NewArea(ghostA, AreaAt(TimeLevel - 1, 1), ghostQ, FlowAt(TimeLevel - 1, 1));
                double t = TimeLevel * TimeStep;
                double hGuess = AreaToDepth(0, newA);
                // For flow BCs the residual is Q - target, so target = Q from hydrograph or normal flow
                double newQ = Channel.UpstreamBoundary.Hydrograph?.GetAt(t) ?? Hydraulics.NormalFlow(
                    Channel.XsAtNode![0].BedSlope ?? 0, Channel.XsAtNode![0].Conveyance(Channel.XsAtNode![0].ZMin + hGuess));
                Depth![TimeLevel, 0] = hGuess;
                Flow![TimeLevel, 0] = newQ;
            }
            else
            {
                // Depth BC: compute Q from Lax, set depth from BC
                double newQ = NewFlow(ghostA, AreaAt(TimeLevel - 1, 1), ghostQ, FlowAt(TimeLevel - 1, 1),
                                      ghostY, WaterLevelAt(TimeLevel - 1, 1), ghostSe, SeAt(TimeLevel - 1, 1));
                double t = TimeLevel * TimeStep;
                // For fixed depth, target is the initial depth
                double targetDepth = Channel.UpstreamBoundary.InitialDepth ?? DepthAt(0, 0);
                Depth![TimeLevel, 0] = targetDepth;
                Flow![TimeLevel, 0] = newQ;
            }
        }

        private void ComputeDownstreamNode()
        {
            int last = NumberOfNodes - 1;
            var (ghostA, ghostQ, ghostY, ghostSe) = DsGhostNode();

            if (Channel.DownstreamBoundary.IsFlowDependent)
            {
                double newA = NewArea(AreaAt(TimeLevel - 1, last - 1), ghostA, FlowAt(TimeLevel - 1, last - 1), ghostQ);
                double t = TimeLevel * TimeStep;
                double hGuess = AreaToDepth(last, newA);
                double newQ = -Channel.DownstreamBoundary.ConditionResidual(hGuess, 0, t);
                Depth![TimeLevel, last] = hGuess;
                Flow![TimeLevel, last] = newQ;
            }
            else
            {
                double newQ = NewFlow(AreaAt(TimeLevel - 1, last - 1), ghostA,
                                      FlowAt(TimeLevel - 1, last - 1), ghostQ,
                                      WaterLevelAt(TimeLevel - 1, last - 1), ghostY,
                                      SeAt(TimeLevel - 1, last - 1), ghostSe);
                double t = TimeLevel * TimeStep;
                double vol = 0.5 * (FlowAt(TimeLevel - 1, last) + newQ) * TimeStep;
                double targetDepth = -(Channel.DownstreamBoundary.ConditionResidual(
                    DepthAt(TimeLevel - 1, last), newQ, t, TimeStep, vol) - DepthAt(TimeLevel - 1, last));
                Depth![TimeLevel, last] = Math.Max(targetDepth, 0.001);
                Flow![TimeLevel, last] = newQ;
            }
        }

        /// <summary>Lax-Friedrichs update for area: A_new = 0.5*(A_{i-1}+A_{i+1}) - dt/(2*dx)*(Q_{i+1}-Q_{i-1})</summary>
        private double NewArea(double A_im1, double A_ip1, double Q_im1, double Q_ip1)
        {
            double avgA = LaxCellAvg(A_ip1, A_im1);
            double dQ_dx = LaxSpatialDiff(Q_ip1, Q_im1);
            return avgA - dQ_dx * TimeStep;
        }

        /// <summary>Lax-Friedrichs update for flow</summary>
        private double NewFlow(double A_im1, double A_ip1, double Q_im1, double Q_ip1,
                               double Y_im1, double Y_ip1, double Se_im1, double Se_ip1)
        {
            double avgQ  = LaxCellAvg(Q_ip1, Q_im1);
            double avgA  = LaxCellAvg(A_ip1, A_im1);
            double avgSe = LaxCellAvg(Se_ip1, Se_im1);

            double dQ2A_dx = LaxSpatialDiff(Q_ip1 * Q_ip1 / Math.Max(A_ip1, 1e-6),
                                             Q_im1 * Q_im1 / Math.Max(A_im1, 1e-6));
            double dY_dx = LaxSpatialDiff(Y_ip1, Y_im1);

            return avgQ - (dQ2A_dx + Hydraulics.G * avgA * (dY_dx + avgSe)) * TimeStep;
        }

        private double LaxSpatialDiff(double ip1, double im1) => 0.5 * (ip1 - im1) / SpatialStep;
        private double LaxCellAvg(double ip1, double im1) => 0.5 * (ip1 + im1);

        /// <summary>Convert cross-sectional area back to water depth using Brentq.</summary>
        private double AreaToDepth(int nodeIdx, double A)
        {
            if (A <= 0) return 0;
            var xs = Channel.XsAtNode![nodeIdx];
            double zMin = xs.ZMin;
            double hMax = 50.0;
            try
            {
                return Hydraulics.Brentq(h => xs.Area(h + zMin) - A, 1e-6, hMax);
            }
            catch
            {
                return A / Math.Max(xs.Width, 1.0); // fallback: rectangular approximation
            }
        }

        private void CheckCflAll()
        {
            for (int i = 0; i < NumberOfNodes; i++)
            {
                double A = AreaAt(TimeLevel, i);
                double Q = FlowAt(TimeLevel, i);
                if (A < 1e-10) continue;
                double V = Q / A;
                double T = Channel.TopWidth(i, WaterLevelAt(TimeLevel, i));
                double D = T > 1e-10 ? A / T : 0;
                double c = Math.Sqrt(Hydraulics.G * Math.Max(D, 0));
                double maxCelerity = Math.Max(Math.Abs(V + c), Math.Abs(V - c));
                if (maxCelerity > NumCelerity)
                    throw new InvalidOperationException(
                        $"CFL condition failed at i={i}, k={TimeLevel}. CFL={maxCelerity / NumCelerity:F3}");
            }
        }
    }
}
