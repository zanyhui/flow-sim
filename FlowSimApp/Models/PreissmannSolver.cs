using System;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace FlowSim.Models
{
    /// <summary>Preissmann implicit finite-difference scheme for Saint-Venant equations.</summary>
    public class PreissmannSolver : Solver
    {
        public double Theta { get; }
        private readonly double[] _unknowns;
        private readonly double[] _R;

        public PreissmannSolver(Channel channel, double theta,
                                double timeStep, double spatialStep, double simulationTime,
                                bool fitSpatialStep = true)
            : base(channel, timeStep, spatialStep, simulationTime, fitSpatialStep)
        {
            Theta = theta;
            _unknowns = new double[NumberOfNodes * 2];
            _R = new double[NumberOfNodes * 2];
            InitializeT0();
            // Flatten initial conditions into unknowns as [h0, Q0, h1, Q1, ...]
            for (int i = 0; i < NumberOfNodes; i++)
            {
                _unknowns[2 * i] = Channel.InitialConditions![i, 0];
                _unknowns[2 * i + 1] = Channel.InitialConditions![i, 1];
            }
        }

        public override void Run(int verbose = 1)
        {
            bool running = true;
            int totalIterations = 0;
            const double tolerance = 1e-4;
            const int maxIter = 100;

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

                int iteration = 0;
                bool converged = false;

                while (!converged)
                {
                    iteration++;
                    if (iteration - 1 >= maxIter)
                        throw new InvalidOperationException($"Failed to converge after {maxIter} iterations.");

                    // Apply current unknowns as guesses for this time level
                    for (int i = 0; i < NumberOfNodes; i++)
                    {
                        Depth![TimeLevel, i] = _unknowns[2 * i];
                        Flow![TimeLevel, i] = _unknowns[2 * i + 1];
                    }

                    ComputeResidualVector();
                    double[,] J = ComputeJacobian();

                    var Jm = Matrix<double>.Build.DenseOfArray(J);
                    var Rv = Vector<double>.Build.DenseOfArray(_R);
                    Vector<double> delta;
                    try { delta = Jm.Solve(-Rv); }
                    catch { throw new InvalidOperationException("Jacobian solve failed."); }

                    for (int k = 0; k < _unknowns.Length; k++)
                        _unknowns[k] += delta[k];

                    double error = 0;
                    for (int k = 0; k < _R.Length; k++) error += _R[k] * _R[k];
                    error = Math.Sqrt(error);

                    if (verbose == 3) Console.WriteLine($">> Iteration #{iteration}: Error = {error}");
                    if (error < tolerance) converged = true;
                }

                if (verbose == 2) Console.WriteLine($">> {iteration} iterations.");
                totalIterations += iteration;
            }

            base.Finalize(verbose);
        }

        private void ComputeResidualVector()
        {
            _R[0] = UpstreamResidual();
            _R[2 * NumberOfNodes - 1] = DownstreamResidual();
            for (int i = 0; i < NumberOfNodes - 1; i++)
            {
                _R[1 + 2 * i] = ContinuityResidual(i);
                _R[2 + 2 * i] = MomentumResidual(i);
            }
        }

        private double[,] ComputeJacobian()
        {
            int size = 2 * NumberOfNodes;
            var J = new double[size, size];

            // Upstream BC row 0: depends on [h0, Q0]
            J[0, 0] = DU_Dh();
            J[0, 1] = DU_DQ();

            // Interior rows
            for (int i = 0; i < NumberOfNodes - 1; i++)
            {
                int row = 1 + 2 * i;
                // Continuity
                J[row, 2 * i]     = DC_Dh_i(i);
                J[row, 2 * i + 1] = DC_DQ_i(i);
                J[row, 2 * (i + 1)]     = DC_Dh_ip1(i);
                J[row, 2 * (i + 1) + 1] = DC_DQ_ip1(i);
                // Momentum
                J[row + 1, 2 * i]     = DM_Dh_i(i);
                J[row + 1, 2 * i + 1] = DM_DQ_i(i);
                J[row + 1, 2 * (i + 1)]     = DM_Dh_ip1(i);
                J[row + 1, 2 * (i + 1) + 1] = DM_DQ_ip1(i);
            }

            // Downstream BC last row: depends on [h_{N-1}, Q_{N-1}]
            J[size - 1, size - 2] = DD_Dh();
            J[size - 1, size - 1] = DD_DQ();

            return J;
        }

        // ---- Residuals ----

        private double UpstreamResidual()
        {
            double t = TimeLevel * TimeStep;
            return Channel.UpstreamBoundary.ConditionResidual(DepthAt(TimeLevel, 0), FlowAt(TimeLevel, 0), t);
        }

        private double DownstreamResidual()
        {
            double t = TimeLevel * TimeStep;
            double vol = 0.5 * (FlowAt(TimeLevel - 1, -1) + FlowAt(TimeLevel, -1)) * TimeStep;
            return Channel.DownstreamBoundary.ConditionResidual(DepthAt(TimeLevel, -1), FlowAt(TimeLevel, -1), t, TimeStep, vol);
        }

        private double ContinuityResidual(int i)
        {
            double dA_dt = TimeDiff(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));

            double dQ_dx = SpatialDiff(
                k1_i1: FlowAt(TimeLevel, i + 1), k1_i: FlowAt(TimeLevel, i),
                k_i1: FlowAt(TimeLevel - 1, i + 1), k_i: FlowAt(TimeLevel - 1, i));

            return dA_dt + dQ_dx;
        }

        private double MomentumResidual(int i)
        {
            double A_k_i   = AreaAt(TimeLevel - 1, i);
            double A_k_i1  = AreaAt(TimeLevel - 1, i + 1);
            double A_k1_i  = AreaAt(TimeLevel, i);
            double A_k1_i1 = AreaAt(TimeLevel, i + 1);

            double Q_k_i   = FlowAt(TimeLevel - 1, i);
            double Q_k_i1  = FlowAt(TimeLevel - 1, i + 1);
            double Q_k1_i  = FlowAt(TimeLevel, i);
            double Q_k1_i1 = FlowAt(TimeLevel, i + 1);

            double dQ_dt = TimeDiff(k1_i1: Q_k1_i1, k1_i: Q_k1_i, k_i1: Q_k_i1, k_i: Q_k_i);

            double dQ2A_dx = SpatialDiff(
                k1_i1: Q_k1_i1 * Q_k1_i1 / Math.Max(A_k1_i1, 1e-6),
                k1_i:  Q_k1_i  * Q_k1_i  / Math.Max(A_k1_i, 1e-6),
                k_i1:  Q_k_i1  * Q_k_i1  / Math.Max(A_k_i1, 1e-6),
                k_i:   Q_k_i   * Q_k_i   / Math.Max(A_k_i, 1e-6));

            double avg_A = CellAvg(k1_i1: A_k1_i1, k1_i: A_k1_i, k_i1: A_k_i1, k_i: A_k_i);

            double dY_dx = SpatialDiff(
                k1_i1: WaterLevelAt(TimeLevel, i + 1),
                k1_i:  WaterLevelAt(TimeLevel, i),
                k_i1:  WaterLevelAt(TimeLevel - 1, i + 1),
                k_i:   WaterLevelAt(TimeLevel - 1, i));

            double avg_Se = CellAvg(
                k1_i1: SeAt(TimeLevel, i + 1), k1_i: SeAt(TimeLevel, i),
                k_i1:  SeAt(TimeLevel - 1, i + 1), k_i: SeAt(TimeLevel - 1, i));

            return dQ_dt + dQ2A_dx + Hydraulics.G * avg_A * (dY_dx + avg_Se);
        }

        // ---- Jacobian terms ----

        private double DU_Dh()
        {
            double h = DepthAt(TimeLevel, 0), Q = FlowAt(TimeLevel, 0);
            double t = TimeLevel * TimeStep;
            return Channel.UpstreamBoundary.Df_Dh(h, Q, t);
        }

        private double DU_DQ()
        {
            double h = DepthAt(TimeLevel, 0), Q = FlowAt(TimeLevel, 0);
            double t = TimeLevel * TimeStep;
            double vol = 0.5 * (Q + FlowAt(TimeLevel - 1, 0));
            return Channel.UpstreamBoundary.Df_DQ(h, Q, TimeStep, t, vol);
        }

        private double DD_Dh()
        {
            double h = DepthAt(TimeLevel, -1), Q = FlowAt(TimeLevel, -1);
            double t = TimeLevel * TimeStep;
            return Channel.DownstreamBoundary.Df_Dh(h, Q, t);
        }

        private double DD_DQ()
        {
            double h = DepthAt(TimeLevel, -1), Q = FlowAt(TimeLevel, -1);
            double t = TimeLevel * TimeStep;
            double vol = 0.5 * (Q + FlowAt(TimeLevel - 1, -1)) * TimeStep;
            return Channel.DownstreamBoundary.Df_DQ(h, Q, TimeStep, t, vol);
        }

        // Continuity Jacobian terms
        private double DC_Dh_i(int i)
            => TimeDiff(k1_i: 1) * DADhAt(TimeLevel, i);

        private double DC_DQ_i(int i)
            => SpatialDiff(k1_i: 1);

        private double DC_Dh_ip1(int i)
            => TimeDiff(k1_i1: 1) * DADhAt(TimeLevel, i + 1);

        private double DC_DQ_ip1(int i)
            => SpatialDiff(k1_i1: 1);

        // Momentum Jacobian terms
        private double DM_Dh_i(int i)
        {
            double A = AreaAt(TimeLevel, i), Q = FlowAt(TimeLevel, i), h = DepthAt(TimeLevel, i);
            double dA_dh = DADhAt(TimeLevel, i);
            double dSe_dA = Channel.DSe_DA(h, Q, i);

            double avg_A = CellAvg(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));
            double dY_dx = SpatialDiff(
                k1_i1: WaterLevelAt(TimeLevel, i + 1), k1_i: WaterLevelAt(TimeLevel, i),
                k_i1: WaterLevelAt(TimeLevel - 1, i + 1), k_i: WaterLevelAt(TimeLevel - 1, i));
            double avg_Se = CellAvg(
                k1_i1: SeAt(TimeLevel, i + 1), k1_i: SeAt(TimeLevel, i),
                k_i1: SeAt(TimeLevel - 1, i + 1), k_i: SeAt(TimeLevel - 1, i));

            double d_dQ2Adx_dA = -SpatialDiff(k1_i: 1) * (Q / Math.Max(A, 1e-6)) * (Q / Math.Max(A, 1e-6));
            double d_avgA_dA   = CellAvg(k1_i: 1);
            double d_dYdx_dh   = SpatialDiff(k1_i: 1);
            double d_avgSe_dA  = CellAvg(k1_i: 1) * dSe_dA;

            return (d_dQ2Adx_dA + Hydraulics.G * (
                avg_A * (d_dYdx_dh + d_avgSe_dA * dA_dh) +
                d_avgA_dA * dA_dh * (dY_dx + avg_Se))) * dA_dh;
        }

        private double DM_DQ_i(int i)
        {
            double A = AreaAt(TimeLevel, i), Q = FlowAt(TimeLevel, i), h = DepthAt(TimeLevel, i);
            double dSe_dQ = Channel.DSe_DQ(h, Q, i);

            double avg_A = CellAvg(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));

            double d_dQdt_dQ  = TimeDiff(k1_i: 1);
            double d_dQ2Adx_dQ = SpatialDiff(k1_i: 1) * 2.0 * Q / Math.Max(A, 1e-6);
            double d_avgSe_dQ  = CellAvg(k1_i: 1) * dSe_dQ;

            return d_dQdt_dQ + d_dQ2Adx_dQ + Hydraulics.G * avg_A * d_avgSe_dQ;
        }

        private double DM_Dh_ip1(int i)
        {
            double A = AreaAt(TimeLevel, i + 1), Q = FlowAt(TimeLevel, i + 1), h = DepthAt(TimeLevel, i + 1);
            double dA_dh = DADhAt(TimeLevel, i + 1);
            double dSe_dA = Channel.DSe_DA(h, Q, i + 1);

            double avg_A = CellAvg(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));
            double dY_dx = SpatialDiff(
                k1_i1: WaterLevelAt(TimeLevel, i + 1), k1_i: WaterLevelAt(TimeLevel, i),
                k_i1: WaterLevelAt(TimeLevel - 1, i + 1), k_i: WaterLevelAt(TimeLevel - 1, i));
            double avg_Se = CellAvg(
                k1_i1: SeAt(TimeLevel, i + 1), k1_i: SeAt(TimeLevel, i),
                k_i1: SeAt(TimeLevel - 1, i + 1), k_i: SeAt(TimeLevel - 1, i));

            double d_dQ2Adx_dA = -SpatialDiff(k1_i1: 1) * (Q / Math.Max(A, 1e-6)) * (Q / Math.Max(A, 1e-6));
            double d_avgA_dA   = CellAvg(k1_i1: 1);
            double d_dYdx_dh   = SpatialDiff(k1_i1: 1);
            double d_avgSe_dA  = CellAvg(k1_i1: 1) * dSe_dA;

            return (d_dQ2Adx_dA + Hydraulics.G * (
                avg_A * (d_dYdx_dh + d_avgSe_dA * dA_dh) +
                d_avgA_dA * dA_dh * (dY_dx + avg_Se))) * dA_dh;
        }

        private double DM_DQ_ip1(int i)
        {
            double A = AreaAt(TimeLevel, i + 1), Q = FlowAt(TimeLevel, i + 1), h = DepthAt(TimeLevel, i + 1);
            double dSe_dQ = Channel.DSe_DQ(h, Q, i + 1);

            double avg_A = CellAvg(
                k1_i1: AreaAt(TimeLevel, i + 1), k1_i: AreaAt(TimeLevel, i),
                k_i1: AreaAt(TimeLevel - 1, i + 1), k_i: AreaAt(TimeLevel - 1, i));

            double d_dQdt_dQ   = TimeDiff(k1_i1: 1);
            double d_dQ2Adx_dQ = SpatialDiff(k1_i1: 1) * 2.0 * Q / Math.Max(A, 1e-6);
            double d_avgSe_dQ  = CellAvg(k1_i1: 1) * dSe_dQ;

            return d_dQdt_dQ + d_dQ2Adx_dQ + Hydraulics.G * avg_A * d_avgSe_dQ;
        }

        // ---- Finite difference operators (matching Python preissmann.py exactly) ----

        /// <summary>
        /// Time derivative operator: (avg_new - avg_old) / dt
        /// where avg = 0.5*(k1_i + k1_i1) for new, 0.5*(k_i + k_i1) for old.
        /// Python: (k1_i1 + k1_i - k_i1 - k_i) / (2 * dt)
        /// </summary>
        private double TimeDiff(double k1_i1 = 0, double k1_i = 0, double k_i1 = 0, double k_i = 0)
            => (k1_i1 + k1_i - k_i1 - k_i) / (2.0 * TimeStep);

        /// <summary>
        /// Preissmann weighted spatial derivative.
        /// Python: theta*(k1_i1-k1_i)/dx + (1-theta)*(k_i1-k_i)/dx
        /// </summary>
        private double SpatialDiff(double k1_i1 = 0, double k1_i = 0, double k_i1 = 0, double k_i = 0)
        {
            double dx_k1 = (k1_i1 - k1_i) / SpatialStep;
            double dx_k  = (k_i1  - k_i)  / SpatialStep;
            return Theta * dx_k1 + (1.0 - Theta) * dx_k;
        }

        /// <summary>
        /// Preissmann weighted cell average.
        /// Python: 0.5*theta*(k1_i1+k1_i) + 0.5*(1-theta)*(k_i1+k_i)
        /// </summary>
        private double CellAvg(double k1_i1 = 0, double k1_i = 0, double k_i1 = 0, double k_i = 0)
            => 0.5 * Theta * (k1_i1 + k1_i) + 0.5 * (1.0 - Theta) * (k_i1 + k_i);
    }
}
