using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlowSim.Models;
using ScottPlot;

namespace FlowSim
{
    public partial class MainForm : Form
    {
        private Solver? _solver;

        public MainForm()
        {
            InitializeComponent();
        }

        private void btnRun_Click(object sender, EventArgs e)
        {
            btnRun.Enabled = false;
            btnSave.Enabled = false;
            txtLog.Clear();
            Log("Building simulation...");

            try
            {
                var solver = BuildSolver();
                _solver = solver;

                Log("Running simulation...");
                // Run on background thread to keep UI responsive
                Task.Run(() =>
                {
                    try
                    {
                        _solver.Run(verbose: 1);
                        Invoke(() =>
                        {
                            Log("Simulation completed successfully.");
                            UpdateResults();
                            btnSave.Enabled = true;
                            btnRun.Enabled = true;
                        });
                    }
                    catch (Exception ex)
                    {
                        Invoke(() =>
                        {
                            Log($"ERROR: {ex.Message}");
                            btnRun.Enabled = true;
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Log($"Setup ERROR: {ex.Message}");
                btnRun.Enabled = true;
            }
        }

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (_solver == null || !_solver.Solved) return;
            using var dlg = new FolderBrowserDialog { Description = "Select output folder" };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _solver.SaveResults(dlg.SelectedPath, "results.xlsx");
                    Log($"Results saved to: {dlg.SelectedPath}");
                }
                catch (Exception ex)
                {
                    Log($"Save ERROR: {ex.Message}");
                }
            }
        }

        private Solver BuildSolver()
        {
            // --- Read channel parameters ---
            double length = (double)numLength.Value;
            double width = (double)numWidth.Value;
            double roughness = (double)numRoughness.Value;
            double initialFlow = (double)numInitialFlow.Value;
            double usBedLevel = (double)numUsBedLevel.Value;
            double dsBedLevel = (double)numDsBedLevel.Value;
            double dsDepth = (double)numDsDepth.Value;

            // --- Upstream BC ---
            Hydrograph? usHydrograph = null;
            BoundaryConditionType usBcType;
            switch (cmbUsBcType.SelectedIndex)
            {
                case 0:
                    usBcType = BoundaryConditionType.FlowHydrograph;
                    usHydrograph = BuildTriangularHydrograph(
                        (double)numPeakFlow.Value,
                        (double)numRiseTime.Value * 3600,
                        (double)numSimTime.Value * 3600);
                    break;
                case 1:
                    usBcType = BoundaryConditionType.NormalDepth;
                    break;
                default:
                    usBcType = BoundaryConditionType.FlowHydrograph;
                    usHydrograph = new Hydrograph(t => initialFlow);
                    break;
            }

            // --- Downstream BC ---
            BoundaryConditionType dsBcType;
            switch (cmbDsBcType.SelectedIndex)
            {
                case 0: dsBcType = BoundaryConditionType.NormalDepth; break;
                case 1: dsBcType = BoundaryConditionType.FixedDepth; break;
                default: dsBcType = BoundaryConditionType.NormalDepth; break;
            }

            double bedSlope = length > 0 ? (usBedLevel - dsBedLevel) / length : 1e-4;

            var usBoundary = new Boundary(usBcType, 0, usBedLevel, null, null, usHydrograph);
            var dsBoundary = new Boundary(dsBcType, length, dsBedLevel, dsDepth);

            var channel = new Channel(usBoundary, dsBoundary, initialFlow, roughness, width,
                                      InitializationMethod.GVFEquation);

            // --- Solver settings ---
            double timeStep = (double)numTimeStep.Value;
            double spatialStep = (double)numSpatialStep.Value;
            double simTime = (double)numSimTime.Value * 3600;

            string solverType = cmbSolverMethod.SelectedItem?.ToString() ?? "Preissmann";
            Solver solver;
            if (solverType == "Lax-Friedrichs")
            {
                solver = new LaxSolver(channel, timeStep, spatialStep, simTime);
            }
            else
            {
                double theta = (double)numTheta.Value;
                solver = new PreissmannSolver(channel, theta, timeStep, spatialStep, simTime);
            }
            return solver;
        }

        private static Hydrograph BuildTriangularHydrograph(double peakFlow, double riseTime, double totalTime)
        {
            double baseFlow = peakFlow * 0.1;
            double fallTime = Math.Min(riseTime * 2, totalTime - riseTime);
            return new Hydrograph(t =>
            {
                if (t <= riseTime) return baseFlow + (peakFlow - baseFlow) * t / riseTime;
                if (t <= riseTime + fallTime) return peakFlow - (peakFlow - baseFlow) * (t - riseTime) / fallTime;
                return baseFlow;
            });
        }

        private void UpdateResults()
        {
            if (_solver == null || !_solver.Solved) return;

            int nk = _solver.TimeLevel + 1;
            int nn = _solver.NumberOfNodes;
            double dt = _solver.TimeStep;
            double[] distance = _solver.Channel.ChAtNode!;

            // --- Plot flow hydrograph at upstream, middle, downstream ---
            plotFlow.Plot.Clear();
            double[] times = new double[nk];
            for (int k = 0; k < nk; k++) times[k] = k * dt / 3600.0;

            int[] plotNodes = { 0, nn / 2, nn - 1 };
            string[] nodeLabels = { "Upstream", "Middle", "Downstream" };
            var colors = new[] { Color.Blue, Color.Green, Color.Red };
            for (int n = 0; n < plotNodes.Length; n++)
            {
                int ni = plotNodes[n];
                double[] qs = new double[nk];
                for (int k = 0; k < nk; k++) qs[k] = _solver.Flow![k, ni];
                var scatter = plotFlow.Plot.AddScatter(times, qs, label: nodeLabels[n]);
                scatter.Color = colors[n];
                scatter.MarkerSize = 0;
            }
            plotFlow.Plot.XLabel("Time (h)");
            plotFlow.Plot.YLabel("Flow (m³/s)");
            plotFlow.Plot.Title("Flow Hydrographs");
            plotFlow.Plot.Legend();
            plotFlow.Refresh();

            // --- Plot water level profile at peak time ---
            plotProfile.Plot.Clear();
            int peakTimeIndex = 0;
            double peakQ = 0;
            for (int k = 0; k < nk; k++)
                if (_solver.Flow![k, 0] > peakQ) { peakQ = _solver.Flow[k, 0]; peakTimeIndex = k; }

            double[] levels = new double[nn];
            double[] bed = _solver.BedProfile!;
            for (int i = 0; i < nn; i++) levels[i] = _solver.Level![peakTimeIndex, i];

            var distKm = new double[distance.Length];
            for (int i = 0; i < distance.Length; i++) distKm[i] = distance[i] / 1000.0;

            var wlScatter = plotProfile.Plot.AddScatter(distKm, levels, label: "Water level @ peak");
            wlScatter.Color = Color.Blue; wlScatter.MarkerSize = 0;
            var bedScatter = plotProfile.Plot.AddScatter(distKm, bed, label: "Bed level");
            bedScatter.Color = Color.SaddleBrown; bedScatter.MarkerSize = 0;
            plotProfile.Plot.XLabel("Distance (km)");
            plotProfile.Plot.YLabel("Elevation (m)");
            plotProfile.Plot.Title("Water Level Profile at Peak");
            plotProfile.Plot.Legend();
            plotProfile.Refresh();

            // --- Summary statistics ---
            double peakIn = 0, peakOut = 0, sumQin = 0, massImbVol = 0;
            for (int k = 0; k < nk; k++)
            {
                double qIn = _solver.Flow![k, 0], qOut = _solver.Flow![k, nn - 1];
                peakIn = Math.Max(peakIn, qIn); peakOut = Math.Max(peakOut, qOut);
                sumQin += qIn; massImbVol += (qIn - qOut) * dt;
            }
            double atten = peakIn > 0 ? (peakIn - peakOut) / peakIn * 100 : 0;
            // mass imbalance % = total_volume_imbalance / total_inflow_volume * 100
            double totalInflowVol = sumQin * dt;
            double massImbPct = totalInflowVol > 0 ? massImbVol / totalInflowVol * 100 : 0;

            gridSummary.Rows.Clear();
            gridSummary.Rows.Add("Spatial step (m)", $"{_solver.SpatialStep:F1}");
            gridSummary.Rows.Add("Time step (s)", $"{_solver.TimeStep:F1}");
            gridSummary.Rows.Add("Number of nodes", _solver.NumberOfNodes);
            gridSummary.Rows.Add("Number of time levels", _solver.TimeLevel + 1);
            gridSummary.Rows.Add("Peak inflow (m³/s)", $"{peakIn:F2}");
            gridSummary.Rows.Add("Peak outflow (m³/s)", $"{peakOut:F2}");
            gridSummary.Rows.Add("Attenuation (%)", $"{atten:F2}");
            gridSummary.Rows.Add("Mass imbalance (%)", $"{massImbPct:F4}");
        }

        private void Log(string message)
        {
            if (txtLog.InvokeRequired)
                txtLog.Invoke(() => txtLog.AppendText(message + Environment.NewLine));
            else
                txtLog.AppendText(message + Environment.NewLine);
        }
    }
}
