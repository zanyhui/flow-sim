using System;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace HydroModel
{
    /// <summary>
    /// Implements the Preissmann implicit finite-difference scheme to solve
    /// the Saint-Venant equations.
    /// Equivalent to preissmann.py.
    /// </summary>
    public class PreissmannSolver : Solver
    {
        public double Theta { get; }

        private Matrix<double>? _jacobian;
        private double[] _residual;
        private double[] _unknowns;

        public PreissmannSolver(double theta, Channel channel, double timeStep,
            double spatialStep, int simulationTime, bool regularization = false,
            bool fitSpatialStep = true)
            : base(channel, timeStep, spatialStep, simulationTime, regularization, fitSpatialStep)
        {
            Theta = theta;
            SolverType = "preissmann";

            int n = NumberOfNodes;
            _residual = new double[n * 2];
            _unknowns = new double[n * 2];

            InitializeT0();

            // Flatten initial conditions
            for (int i = 0; i < n; i++)
            {
                _unknowns[2 * i] = Channel.InitialConditions![i, 0];
                _unknowns[2 * i + 1] = Channel.InitialConditions[i, 1];
            }
        }

        public override void Run(double tolerance = 1e-4, int verbose = 1, int maxIter = 100)
        {
            bool running = true;

            while (running)
            {
                TimeLevel++;
                NewTimeLevel = true;

                if (TimeLevel >= NumberOfTimeLevels)
                {
                    TimeLevel = NumberOfTimeLevels - 1;
                    break;
                }

                if (verbose >= 1)
                    Console.WriteLine($"\n> Time level #{TimeLevel}");

                int iteration = 0;
                bool converged = false;

                while (!converged)
                {
                    iteration++;
                    if (iteration - 1 >= maxIter)
                        throw new InvalidOperationException(
                            $"Convergence within {iteration - 1} iterations couldn't be achieved.");

                    // Update guesses from current unknowns vector
                    int nn = NumberOfNodes;
                    for (int i = 0; i < nn; i++)
                    {
                        Depth[TimeLevel, i] = _unknowns[2 * i];
                        Flow[TimeLevel, i] = _unknowns[2 * i + 1];
                    }

                    ComputeResidualVector();
                    ComputeJacobian();

                    // Solve J * delta = -R
                    var R = Vector<double>.Build.DenseOfArray(_residual);
                    var delta = _jacobian!.Solve(-R);

                    for (int i = 0; i < _unknowns.Length; i++)
                        _unknowns[i] += delta[i];

                    double error = Utility.EuclideanNorm(_residual);

                    if (verbose == 3)
                        Console.WriteLine($">> Iteration #{iteration}: Error = {error}");

                    if (error < tolerance)
                        converged = true;
                    else
                        NewTimeLevel = false;
                }

                if (verbose == 2)
                    Console.WriteLine($">> {iteration} iterations.");
            }

            Finalize(verbose);
        }

        private void ComputeResidualVector()
        {
            int n = NumberOfNodes;
            _residual[0] = UpstreamResidual();
            _residual[2 * n - 1] = DownstreamResidual();

            for (int i = 0; i < n - 1; i++)
            {
                _residual[1 + 2 * i] = ContinuityResidual(i);
                _residual[2 + 2 * i] = MomentumResidual(i);
            }
        }

        private void ComputeJacobian()
        {
            int n = NumberOfNodes;
            int size = 2 * n;

            if (_jacobian == null)
                _jacobian = SparseMatrix.Create(size, size, 0.0);
            else
                _jacobian.Clear();

            // Upstream BC row 0
            _jacobian[0, 0] = DU_Dh();
            _jacobian[0, 1] = DU_DQ();

            // Interior equations
            for (int i = 0; i < n - 1; i++)
            {
                int row = 1 + 2 * i;
                int col = 2 * i; // row - 1

                // Continuity
                _jacobian[row, col] = DC_Dh_i(i);
                _jacobian[row, col + 1] = DC_DQ_i(i);
                _jacobian[row, col + 2] = DC_Dh_ip1(i);
                _jacobian[row, col + 3] = DC_DQ_ip1(i);

                // Momentum
                _jacobian[row + 1, col] = DM_Dh_i(i);
                _jacobian[row + 1, col + 1] = DM_DQ_i(i);
                _jacobian[row + 1, col + 2] = DM_Dh_ip1(i);
                _jacobian[row + 1, col + 3] = DM_DQ_ip1(i);
            }

            // Downstream BC last row
            int lastRow = 2 * n - 1;
            _jacobian[lastRow, lastRow - 1] = DD_Dh();
            _jacobian[lastRow, lastRow] = DD_DQ();
        }

        private double UpstreamResidual()
        {
            double time = TimeLevel * TimeStep;
            return Channel.UpstreamBoundary.ConditionResidual(
                DepthAt(TimeLevel, 0), FlowAt(TimeLevel, 0), time);
        }

        private double DownstreamResidual()
        {
            double time = TimeLevel * TimeStep;
            int last = NumberOfNodes - 1;
            double volume = 0.5 * (FlowAt(TimeLevel - 1, last) + FlowAt(TimeLevel, last)) * TimeStep;
            return Channel.DownstreamBoundary.ConditionResidual(
                DepthAt(TimeLevel, last), FlowAt(TimeLevel, last),
                time, TimeStep, volume);
        }

        private double ContinuityResidual(int i)
        {
            double dA_dt = TimeDiff(
                AreaAt(TimeLevel - 1, i), AreaAt(TimeLevel - 1, i + 1),
                AreaAt(TimeLevel, i), AreaAt(TimeLevel, i + 1));

            double dQ_dx = SpatialDiff(
                FlowAt(TimeLevel - 1, i), FlowAt(TimeLevel - 1, i + 1),
                FlowAt(TimeLevel, i), FlowAt(TimeLevel, i + 1));

            return dA_dt + dQ_dx;
        }

        private double MomentumResidual(int i)
        {
            double dQ_dt = TimeDiff(
                FlowAt(TimeLevel - 1, i), FlowAt(TimeLevel - 1, i + 1),
                FlowAt(TimeLevel, i), FlowAt(TimeLevel, i + 1));

            double a_k_i = AreaAt(TimeLevel - 1, i);
            double a_k_ip1 = AreaAt(TimeLevel - 1, i + 1);
            double a_k1_i = AreaAt(TimeLevel, i);
            double a_k1_ip1 = AreaAt(TimeLevel, i + 1);

            double dQ2A_dx = SpatialDiff(
                FlowAt(TimeLevel - 1, i) * FlowAt(TimeLevel - 1, i) / a_k_i,
                FlowAt(TimeLevel - 1, i + 1) * FlowAt(TimeLevel - 1, i + 1) / a_k_ip1,
                FlowAt(TimeLevel, i) * FlowAt(TimeLevel, i) / a_k1_i,
                FlowAt(TimeLevel, i + 1) * FlowAt(TimeLevel, i + 1) / a_k1_ip1);

            double avgA = CellAvg(a_k_i, a_k_ip1, a_k1_i, a_k1_ip1);

            double dY_dx = SpatialDiff(
                WaterLevelAt(TimeLevel - 1, i), WaterLevelAt(TimeLevel - 1, i + 1),
                WaterLevelAt(TimeLevel, i), WaterLevelAt(TimeLevel, i + 1));

            double avgSe = CellAvg(
                SeAt(TimeLevel - 1, i), SeAt(TimeLevel - 1, i + 1),
                SeAt(TimeLevel, i), SeAt(TimeLevel, i + 1));

            return dQ_dt + dQ2A_dx + Hydraulics.G * avgA * (dY_dx + avgSe);
        }

        // Jacobian for upstream BC
        private double DU_Dh()
        {
            double h = DepthAt(TimeLevel, 0);
            double t = TimeLevel * TimeStep;
            double Q = FlowAt(TimeLevel, 0);
            return Channel.UpstreamBoundary.DfDh(h, Q, t);
        }

        private double DU_DQ()
        {
            double t = TimeLevel * TimeStep;
            double Q = FlowAt(TimeLevel, 0);
            double h = DepthAt(TimeLevel, 0);
            double volume = 0.5 * (Q + FlowAt(TimeLevel - 1, 0));
            return Channel.UpstreamBoundary.DfDQ(h, Q, TimeStep, t, volume);
        }

        // Continuity Jacobian
        private double DC_Dh_i(int i) =>
            TimeDiff(0, 0, 1, 0) * DAreaDDepth(TimeLevel, i);
        private double DC_DQ_i(int i) =>
            SpatialDiff(0, 0, 1, 0);
        private double DC_Dh_ip1(int i) =>
            TimeDiff(0, 0, 0, 1) * DAreaDDepth(TimeLevel, i + 1);
        private double DC_DQ_ip1(int i) =>
            SpatialDiff(0, 0, 0, 1);

        // Momentum Jacobian for node i
        private double DM_Dh_i(int i)
        {
            double A = AreaAt(TimeLevel, i);
            double Q = FlowAt(TimeLevel, i);
            double h = DepthAt(TimeLevel, i);
            double dAdH = DAreaDDepth(TimeLevel, i);
            double dSedA = Channel.DSeDArea(h, Q, i);

            double avgA = CellAvg(
                AreaAt(TimeLevel - 1, i), AreaAt(TimeLevel - 1, i + 1),
                AreaAt(TimeLevel, i), AreaAt(TimeLevel, i + 1));
            double dYdx = SpatialDiff(
                WaterLevelAt(TimeLevel - 1, i), WaterLevelAt(TimeLevel - 1, i + 1),
                WaterLevelAt(TimeLevel, i), WaterLevelAt(TimeLevel, i + 1));
            double avgSe = CellAvg(
                SeAt(TimeLevel - 1, i), SeAt(TimeLevel - 1, i + 1),
                SeAt(TimeLevel, i), SeAt(TimeLevel, i + 1));

            double d_dQ2Adx_dA = -SpatialDiff(0, 0, 1, 0) * (Q / A) * (Q / A);
            double d_avgA_dA = CellAvg(0, 0, 1, 0);
            double d_dYdx_dh = SpatialDiff(0, 0, 1, 0);
            double d_avgSe_dA = CellAvg(0, 0, 1, 0) * dSedA;

            return d_dQ2Adx_dA * dAdH + Hydraulics.G * (
                avgA * (d_dYdx_dh + d_avgSe_dA * dAdH) + d_avgA_dA * dAdH * (dYdx + avgSe));
        }

        private double DM_DQ_i(int i)
        {
            double A = AreaAt(TimeLevel, i);
            double Q = FlowAt(TimeLevel, i);
            double h = DepthAt(TimeLevel, i);
            double dSedQ = Channel.DSeDFlow(h, Q, i);

            double avgA = CellAvg(
                AreaAt(TimeLevel - 1, i), AreaAt(TimeLevel - 1, i + 1),
                AreaAt(TimeLevel, i), AreaAt(TimeLevel, i + 1));

            double d_dQdt_dQ = TimeDiff(0, 0, 1, 0);
            double d_dQ2Adx_dQ = SpatialDiff(0, 0, 1, 0) * 2.0 * Q / A;
            double d_avgSe_dQ = CellAvg(0, 0, 1, 0) * dSedQ;

            return d_dQdt_dQ + d_dQ2Adx_dQ + Hydraulics.G * avgA * d_avgSe_dQ;
        }

        // Momentum Jacobian for node i+1
        private double DM_Dh_ip1(int i)
        {
            double A = AreaAt(TimeLevel, i + 1);
            double Q = FlowAt(TimeLevel, i + 1);
            double h = DepthAt(TimeLevel, i + 1);
            double dAdH = DAreaDDepth(TimeLevel, i + 1);
            double dSedA = Channel.DSeDArea(h, Q, i + 1);

            double avgA = CellAvg(
                AreaAt(TimeLevel - 1, i), AreaAt(TimeLevel - 1, i + 1),
                AreaAt(TimeLevel, i), AreaAt(TimeLevel, i + 1));
            double dYdx = SpatialDiff(
                WaterLevelAt(TimeLevel - 1, i), WaterLevelAt(TimeLevel - 1, i + 1),
                WaterLevelAt(TimeLevel, i), WaterLevelAt(TimeLevel, i + 1));
            double avgSe = CellAvg(
                SeAt(TimeLevel - 1, i), SeAt(TimeLevel - 1, i + 1),
                SeAt(TimeLevel, i), SeAt(TimeLevel, i + 1));

            double d_dQ2Adx_dA = -SpatialDiff(0, 0, 0, 1) * (Q / A) * (Q / A);
            double d_avgA_dA = CellAvg(0, 0, 0, 1);
            double d_dYdx_dh = SpatialDiff(0, 0, 0, 1);
            double d_avgSe_dA = CellAvg(0, 0, 0, 1) * dSedA;

            return d_dQ2Adx_dA * dAdH + Hydraulics.G * (
                avgA * (d_dYdx_dh + d_avgSe_dA * dAdH) + d_avgA_dA * dAdH * (dYdx + avgSe));
        }

        private double DM_DQ_ip1(int i)
        {
            double A = AreaAt(TimeLevel, i + 1);
            double Q = FlowAt(TimeLevel, i + 1);
            double h = DepthAt(TimeLevel, i + 1);
            double dSedQ = Channel.DSeDFlow(h, Q, i + 1);

            double avgA = CellAvg(
                AreaAt(TimeLevel - 1, i), AreaAt(TimeLevel - 1, i + 1),
                AreaAt(TimeLevel, i), AreaAt(TimeLevel, i + 1));

            double d_dQdt_dQ = TimeDiff(0, 0, 0, 1);
            double d_dQ2Adx_dQ = SpatialDiff(0, 0, 0, 1) * 2.0 * Q / A;
            double d_avgSe_dQ = CellAvg(0, 0, 0, 1) * dSedQ;

            return d_dQdt_dQ + d_dQ2Adx_dQ + Hydraulics.G * avgA * d_avgSe_dQ;
        }

        // Downstream BC Jacobian
        private double DD_Dh()
        {
            int last = NumberOfNodes - 1;
            double h = DepthAt(TimeLevel, last);
            double Q = FlowAt(TimeLevel, last);
            double t = TimeLevel * TimeStep;
            return Channel.DownstreamBoundary.DfDh(h, Q, t);
        }

        private double DD_DQ()
        {
            int last = NumberOfNodes - 1;
            double t = TimeLevel * TimeStep;
            double h = DepthAt(TimeLevel, last);
            double Q = FlowAt(TimeLevel, last);
            double volume = 0.5 * (FlowAt(TimeLevel - 1, last) + Q) * TimeStep;
            return Channel.DownstreamBoundary.DfDQ(h, Q, TimeStep, t, volume);
        }

        // -----------------------------------------------------------------
        // Preissmann finite-difference operators
        // (k = previous time level, k+1 = current; i = current node, i+1 = next)
        // All params: (k_i, k_i1, k1_i, k1_i1)
        // -----------------------------------------------------------------

        private double TimeDiff(double kI, double kI1, double k1I, double k1I1)
            => (k1I1 + k1I - kI1 - kI) / (2.0 * TimeStep);

        private double SpatialDiff(double kI, double kI1, double k1I, double k1I1)
        {
            double dxK1 = (k1I1 - k1I) / SpatialStep;
            double dxK = (kI1 - kI) / SpatialStep;
            return Theta * dxK1 + (1.0 - Theta) * dxK;
        }

        private double CellAvg(double kI, double kI1, double k1I, double k1I1)
        {
            return 0.5 * Theta * (k1I1 + k1I) + 0.5 * (1.0 - Theta) * (kI1 + kI);
        }
    }
}
