using System;
using System.Collections.Generic;

namespace HydroModel
{
    /// <summary>
    /// Abstract base class for solvers of the Saint-Venant equations.
    /// Equivalent to solver.py.
    /// </summary>
    public abstract class Solver
    {
        protected Channel Channel { get; }
        protected double TimeStep { get; private set; }
        protected double SpatialStep { get; private set; }
        protected int TimeLevel { get; set; }
        protected int NumberOfNodes { get; private set; }
        protected int NumberOfTimeLevels { get; }
        protected double NumCelerity { get; private set; }

        // Results arrays [time_levels, nodes]
        protected double[,] Flow;
        protected double[,] Depth;

        protected string? SolverType;
        protected bool Solved;
        protected bool NewTimeLevel;
        protected double TotalSimDuration;
        protected bool Regularization { get; }
        protected double Eps = 1e-4;

        // Post-processing results
        public double[,]? LevelResult { get; private set; }
        public double[,]? FlowResult { get; private set; }
        public double[,]? DepthResult { get; private set; }
        public double[,]? AreaResult { get; private set; }
        public double[,]? TopWidthResult { get; private set; }
        public double[,]? VelocityResult { get; private set; }
        public double[,]? FroudeNumberResult { get; private set; }
        public double[,]? WaveCelerityResult { get; private set; }
        public double[,]? AmplitudeResult { get; private set; }
        public double[]? PeakAmplitudeResult { get; private set; }
        public double[]? BedProfile { get; private set; }
        public double[]? StorageStage { get; private set; }
        public double[]? StorageOutflow { get; private set; }

        protected Solver(Channel channel, double timeStep, double spatialStep,
            int simulationTime, bool regularization = false, bool fitSpatialStep = true)
        {
            Channel = channel;
            TimeStep = timeStep;
            SpatialStep = spatialStep;
            TimeLevel = 0;
            NumberOfNodes = (int)(channel.Length / spatialStep) + 1;
            NumberOfTimeLevels = (int)(simulationTime / timeStep) + 1;
            Regularization = regularization;

            if (fitSpatialStep)
                FitSpatialStep();

            Channel.InitializeConditions(NumberOfNodes);
            NumCelerity = SpatialStep / TimeStep;

            Flow = new double[NumberOfTimeLevels, NumberOfNodes];
            Depth = new double[NumberOfTimeLevels, NumberOfNodes];

            Solved = false;
            NewTimeLevel = false;
            TotalSimDuration = 0;
        }

        private void FitSpatialStep()
        {
            NumberOfNodes = (int)Math.Round(Channel.Length / SpatialStep) + 1;
            SpatialStep = Channel.Length / (NumberOfNodes - 1);
        }

        public abstract void Run(double tolerance = 1e-4, int verbose = 1, int maxIter = 100);

        protected void InitializeT0()
        {
            for (int i = 0; i < NumberOfNodes; i++)
            {
                Depth[0, i] = Channel.InitialConditions![i, 0];
                Flow[0, i] = Channel.InitialConditions[i, 1];
            }
        }

        protected void PrepareResults()
        {
            int nt = TimeLevel + 1;
            int nx = NumberOfNodes;

            // Trim arrays to actual time levels computed
            if (TimeLevel + 1 < NumberOfTimeLevels)
            {
                Flow = TrimArray(Flow, nt, nx);
                Depth = TrimArray(Depth, nt, nx);
            }

            // Bed profile
            BedProfile = new double[nx];
            for (int i = 0; i < nx; i++)
                BedProfile[i] = Channel.BedLevelAt(i);

            // Level = Depth + BedProfile
            LevelResult = new double[nt, nx];
            DepthResult = new double[nt, nx];
            FlowResult = new double[nt, nx];
            AreaResult = new double[nt, nx];
            TopWidthResult = new double[nt, nx];
            FroudeNumberResult = new double[nt, nx];

            for (int k = 0; k < nt; k++)
            {
                for (int i = 0; i < nx; i++)
                {
                    LevelResult[k, i] = BedProfile[i] + Depth[k, i];
                    DepthResult[k, i] = Depth[k, i];
                    FlowResult[k, i] = Flow[k, i];

                    double hw = WaterLevelAt(k, i);
                    double A = Channel.AreaAt(i, hw);
                    double T = Channel.TopWidthAt(i, hw);

                    AreaResult[k, i] = A;
                    TopWidthResult[k, i] = T;
                    FroudeNumberResult[k, i] = Hydraulics.FroudeNumber(T, A, Flow[k, i]);
                }
            }

            // Velocity = Flow / Area
            VelocityResult = new double[nt, nx];
            WaveCelerityResult = new double[nt, nx];
            for (int k = 0; k < nt; k++)
            {
                for (int i = 0; i < nx; i++)
                {
                    double A = AreaResult![k, i];
                    double T = TopWidthResult![k, i];
                    double Q = FlowResult![k, i];
                    VelocityResult[k, i] = A > 0 ? Q / A : 0;
                    double D = T > 0 ? A / T : 0;
                    WaveCelerityResult[k, i] = VelocityResult[k, i] + Math.Sqrt(Hydraulics.G * Math.Max(D, 0));
                }
            }

            // Amplitude = Depth - initial depth
            AmplitudeResult = new double[nt, nx];
            PeakAmplitudeResult = new double[nx];
            for (int i = 0; i < nx; i++)
            {
                double refDepth = Depth[0, i];
                double peak = 0;
                for (int k = 0; k < nt; k++)
                {
                    double amp = Depth[k, i] - refDepth;
                    AmplitudeResult[k, i] = amp;
                    if (amp > peak) peak = amp;
                }
                PeakAmplitudeResult[i] = peak;
            }

            // Lumped storage post-processing
            var ls = Channel.DownstreamBoundary.LumpedStorage;
            if (ls != null)
            {
                double hw0 = WaterLevelAt(0, nx - 1);
                double A0 = AreaResult![0, nx - 1];
                double Q0 = Flow[0, nx - 1];
                double n0 = Channel.XsAtNode![nx - 1].GetEquivalentN(hw0);
                double R0 = Channel.XsAtNode[nx - 1].HydraulicRadius(hw0);
                double initStorageStage = hw0 - ls.EnergyLoss(A0, Q0, n0, R0);

                ls.StageHydrograph.Insert(0, new[] { 0.0, initStorageStage });

                double[,] ssArr = new double[ls.StageHydrograph.Count, 2];
                for (int i = 0; i < ls.StageHydrograph.Count; i++)
                {
                    ssArr[i, 0] = ls.StageHydrograph[i][0];
                    ssArr[i, 1] = ls.StageHydrograph[i][1];
                }

                StorageStage = new double[nt];
                for (int k = 0; k < nt; k++)
                    StorageStage[k] = ls.StageHydrograph[Math.Min(k, ls.StageHydrograph.Count - 1)][1];

                StorageOutflow = new double[nt];
                StorageOutflow[0] = ls.RatingCurve == null ? 0
                    : Math.Min(Flow[0, nx - 1], ls.RatingCurve.Discharge(StorageStage[0], 0));

                for (int k = 1; k < nt; k++)
                {
                    double avgInflow = 0.5 * (Flow[k - 1, nx - 1] + Flow[k, nx - 1]);
                    double volChange = ls.NetVolChange(StorageStage[k - 1], StorageStage[k]);
                    double avgOutflow = avgInflow - volChange / TimeStep;
                    StorageOutflow[k] = avgInflow > 0 ? avgOutflow * Flow[k, nx - 1] / avgInflow : 0;
                }
            }
        }

        protected void Finalize(int verbose)
        {
            Solved = true;
            TotalSimDuration = TimeLevel * TimeStep;
            PrepareResults();

            if (verbose >= 1)
                Console.WriteLine("Simulation completed successfully.");
        }

        protected double DepthAt(int k, int i)
        {
            int kk = k == -1 ? TimeLevel - 1 : k < 0 ? TimeLevel + k : k;
            return Depth[kk, i < 0 ? NumberOfNodes + i : i];
        }

        protected double FlowAt(int k, int i)
        {
            int kk = k == -1 ? TimeLevel - 1 : k < 0 ? TimeLevel + k : k;
            return Flow[kk, i < 0 ? NumberOfNodes + i : i];
        }

        protected double AreaAt(int k, int i)
        {
            return Channel.AreaAt(i < 0 ? NumberOfNodes + i : i, WaterLevelAt(k, i));
        }

        protected double WaterLevelAt(int k, int i)
        {
            int ni = i < 0 ? NumberOfNodes + i : i;
            return Channel.BedLevelAt(ni) + DepthAt(k, ni);
        }

        protected double SeAt(int k, int i)
        {
            int ni = i < 0 ? NumberOfNodes + i : i;
            return Channel.Se(DepthAt(k, ni), FlowAt(k, ni), ni);
        }

        protected double DAreaDDepth(int k, int i)
        {
            int ni = i < 0 ? NumberOfNodes + i : i;
            return Channel.DAreaDDepthAt(ni, WaterLevelAt(k, ni));
        }

        private static double[,] TrimArray(double[,] src, int rows, int cols)
        {
            var result = new double[rows, cols];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    result[i, j] = src[i, j];
            return result;
        }

        public double GetTimeStep() => TimeStep;
        public double GetSpatialStep() => SpatialStep;
        public int GetNumberOfNodes() => NumberOfNodes;
    }
}
