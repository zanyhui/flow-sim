using System;

namespace HydroModel
{
    /// <summary>
    /// Implements the Lax-Friedrichs explicit finite-difference scheme to solve
    /// the Saint-Venant equations.
    /// Equivalent to lax.py.
    /// </summary>
    public class LaxSolver : Solver
    {
        public string[] SecondaryBC { get; }

        /// <summary>
        /// Initializes the Lax-Friedrichs solver.
        /// </summary>
        /// <param name="secondaryBC">
        /// Two-element array specifying secondary boundary approximations at
        /// upstream and downstream ends. Options: "constant", "mirror", "linear".
        /// </param>
        public LaxSolver(Channel channel, double timeStep, double spatialStep,
            int simulationTime, string[]? secondaryBC = null,
            bool regularization = false, bool fitSpatialStep = true)
            : base(channel, timeStep, spatialStep, simulationTime, regularization, fitSpatialStep)
        {
            SecondaryBC = secondaryBC ?? new[] { "constant", "constant" };
            SolverType = "lax";
            InitializeT0();
        }

        public override void Run(double tolerance = 1e-4, int verbose = 1, int maxIter = 100)
        {
            bool running = true;

            while (running)
            {
                TimeLevel++;
                if (TimeLevel >= NumberOfTimeLevels)
                {
                    TimeLevel = NumberOfTimeLevels - 1;
                    break;
                }

                if (verbose >= 1)
                    Console.WriteLine($"\n> Time level #{TimeLevel}");

                for (int i = 0; i < NumberOfNodes; i++)
                    ComputeNode(i);

                CheckCflAll();
            }

            Finalize(verbose);
        }

        private (double A, double Q, double Y, double Se) UsGhostNode()
        {
            switch (SecondaryBC[0])
            {
                case "mirror":
                    return (AreaAt(TimeLevel - 1, 1), FlowAt(TimeLevel - 1, 1),
                        WaterLevelAt(TimeLevel - 1, 1), SeAt(TimeLevel - 1, 1));
                case "linear":
                    return (2 * AreaAt(TimeLevel - 1, 0) - AreaAt(TimeLevel - 1, 1),
                        2 * FlowAt(TimeLevel - 1, 0) - FlowAt(TimeLevel - 1, 1),
                        2 * WaterLevelAt(TimeLevel - 1, 0) - WaterLevelAt(TimeLevel - 1, 1),
                        2 * SeAt(TimeLevel - 1, 0) - SeAt(TimeLevel - 1, 1));
                default: // "constant"
                    return (AreaAt(TimeLevel - 1, 0), FlowAt(TimeLevel - 1, 0),
                        WaterLevelAt(TimeLevel - 1, 0), SeAt(TimeLevel - 1, 0));
            }
        }

        private (double A, double Q, double Y, double Se) DsGhostNode()
        {
            int last = NumberOfNodes - 1;
            switch (SecondaryBC[1])
            {
                case "mirror":
                    return (AreaAt(TimeLevel - 1, last - 1), FlowAt(TimeLevel - 1, last - 1),
                        WaterLevelAt(TimeLevel - 1, last - 1), SeAt(TimeLevel - 1, last - 1));
                case "linear":
                    return (2 * AreaAt(TimeLevel - 1, last) - AreaAt(TimeLevel - 1, last - 1),
                        2 * FlowAt(TimeLevel - 1, last) - FlowAt(TimeLevel - 1, last - 1),
                        2 * WaterLevelAt(TimeLevel - 1, last) - WaterLevelAt(TimeLevel - 1, last - 1),
                        2 * SeAt(TimeLevel - 1, last) - SeAt(TimeLevel - 1, last - 1));
                default: // "constant"
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
                Depth[TimeLevel, i] = NewDepth(
                    AreaAt(TimeLevel - 1, i - 1), AreaAt(TimeLevel - 1, i + 1),
                    FlowAt(TimeLevel - 1, i - 1), FlowAt(TimeLevel - 1, i + 1));

                Flow[TimeLevel, i] = NewFlow(
                    AreaAt(TimeLevel - 1, i - 1), AreaAt(TimeLevel - 1, i + 1),
                    FlowAt(TimeLevel - 1, i - 1), FlowAt(TimeLevel - 1, i + 1),
                    WaterLevelAt(TimeLevel - 1, i - 1), WaterLevelAt(TimeLevel - 1, i + 1),
                    SeAt(TimeLevel - 1, i - 1), SeAt(TimeLevel - 1, i + 1));
            }
        }

        private void ComputeUpstreamNode()
        {
            var (ghostA, ghostQ, ghostY, ghostSe) = UsGhostNode();

            if (Channel.UpstreamBoundary.ConditionType())
            {
                double newA = NewArea(ghostA, AreaAt(TimeLevel - 1, 1),
                    ghostQ, FlowAt(TimeLevel - 1, 1));
                double hw = Channel.BedLevelAt(0) + newA / (Channel.Width ?? 1);
                double Q = -Channel.UpstreamBoundary.ConditionResidual(
                    depth: newA / (Channel.Width ?? 1),
                    flow: 0,
                    time: TimeLevel * TimeStep);

                Depth[TimeLevel, 0] = hw - Channel.BedLevelAt(0);
                Flow[TimeLevel, 0] = Q;
            }
            else
            {
                double Q = NewFlow(ghostA, AreaAt(TimeLevel - 1, 1),
                    ghostQ, FlowAt(TimeLevel - 1, 1),
                    ghostY, WaterLevelAt(TimeLevel - 1, 1),
                    ghostSe, SeAt(TimeLevel - 1, 1));
                double depth = -Channel.UpstreamBoundary.ConditionResidual(
                    depth: 0, flow: Q, time: TimeLevel * TimeStep);

                Depth[TimeLevel, 0] = depth;
                Flow[TimeLevel, 0] = Q;
            }
        }

        private void ComputeDownstreamNode()
        {
            int last = NumberOfNodes - 1;
            var (ghostA, ghostQ, ghostY, ghostSe) = DsGhostNode();

            if (Channel.DownstreamBoundary.ConditionType())
            {
                double newA = NewArea(AreaAt(TimeLevel - 1, last - 1), ghostA,
                    FlowAt(TimeLevel - 1, last - 1), ghostQ);
                double Q = -Channel.DownstreamBoundary.ConditionResidual(
                    depth: newA / (Channel.Width ?? 1),
                    flow: 0,
                    time: TimeLevel * TimeStep);

                Depth[TimeLevel, last] = newA / (Channel.Width ?? 1);
                Flow[TimeLevel, last] = Q;
            }
            else
            {
                double Q = NewFlow(
                    AreaAt(TimeLevel - 1, last - 1), ghostA,
                    FlowAt(TimeLevel - 1, last - 1), ghostQ,
                    WaterLevelAt(TimeLevel - 1, last - 1), ghostY,
                    SeAt(TimeLevel - 1, last - 1), ghostSe);
                double depth = -Channel.DownstreamBoundary.ConditionResidual(
                    depth: 0, flow: Q, time: TimeLevel * TimeStep,
                    duration: TimeStep,
                    volIn: 0.5 * (FlowAt(TimeLevel - 1, last) + Q) * TimeStep);

                Depth[TimeLevel, last] = depth;
                Flow[TimeLevel, last] = Q;
            }
        }

        private double NewArea(double aIm1, double aIp1, double qIm1, double qIp1)
        {
            double avgA = 0.5 * (aIm1 + aIp1);
            double dQdx = 0.5 * (qIp1 - qIm1) / SpatialStep;
            return -dQdx * TimeStep + avgA;
        }

        private double NewFlow(double aIm1, double aIp1, double qIm1, double qIp1,
            double yIm1, double yIp1, double seIm1, double seIp1)
        {
            double avgA = 0.5 * (aIm1 + aIp1);
            double avgQ = 0.5 * (qIm1 + qIp1);
            double avgSe = 0.5 * (seIm1 + seIp1);

            double dQ2Adx = 0.5 * (qIp1 * qIp1 / aIp1 - qIm1 * qIm1 / aIm1) / SpatialStep;
            double dYdx = 0.5 * (yIp1 - yIm1) / SpatialStep;

            return -(dQ2Adx + Hydraulics.G * avgA * (dYdx + avgSe)) * TimeStep + avgQ;
        }

        private double NewDepth(double aIm1, double aIp1, double qIm1, double qIp1)
        {
            double newA = NewArea(aIm1, aIp1, qIm1, qIp1);
            // Return depth: area / width (for simple rectangular channel)
            // For irregular sections, depth = A / T (approximate)
            return newA; // stored as A temporarily; see ComputeNode where Depth is set
        }

        private void CheckCflAll()
        {
            for (int i = 0; i < NumberOfNodes; i++)
            {
                double h = DepthAt(TimeLevel, i);
                double A = AreaAt(TimeLevel, i);
                if (A <= 0) continue;
                double V = FlowAt(TimeLevel, i) / A;

                double analyticalCelerity = Math.Max(
                    V + Math.Sqrt(Hydraulics.G * Math.Max(h, 0)),
                    V - Math.Sqrt(Hydraulics.G * Math.Max(h, 0)));

                if (NumCelerity < analyticalCelerity)
                    throw new InvalidOperationException(
                        $"CFL condition failed at i={i}, k={TimeLevel}. " +
                        $"CFL number = {analyticalCelerity / NumCelerity}");
            }
        }
    }
}
