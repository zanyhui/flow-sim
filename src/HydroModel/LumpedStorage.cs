using System;
using System.Collections.Generic;
using MathNet.Numerics;

namespace HydroModel
{
    /// <summary>
    /// Represents a lumped (0D) storage element such as a reservoir.
    /// Equivalent to lumped_storage.py.
    /// </summary>
    public class LumpedStorage
    {
        public RatingCurve? RatingCurve { get; set; }
        public double? SurfaceArea { get; set; }
        public double? MinStage { get; set; }

        public List<double[]> StageHydrograph { get; } = new();

        private double[,]? _areaCurve;
        private double[]? _areaCurveStages;
        private double[]? _areaCurveAreas;
        private double[]? _areaGradient;

        private double _alpha = 1.0;
        private double _beta = 0.0;

        public double YMin { get; private set; }
        public double YMax { get; private set; }
        public bool CaptureLosses { get; set; } = false;
        public double Cc { get; set; } = 0.5;
        public double KQ { get; set; } = 0;
        public double? ReservoirLength { get; set; }

        public LumpedStorage((double min, double max)? solutionBoundaries = null,
            double? surfaceArea = null, double? minStage = null, RatingCurve? ratingCurve = null)
        {
            RatingCurve = ratingCurve;
            SurfaceArea = surfaceArea;
            MinStage = minStage;

            if (solutionBoundaries.HasValue)
            {
                YMin = solutionBoundaries.Value.min;
                YMax = solutionBoundaries.Value.max;
            }
        }

        /// <summary>Computes the new reservoir stage via mass balance.</summary>
        public double MassBalance(double duration, double volIn, double? yOld = null, double? time = null)
        {
            double yOldVal = yOld ?? YMin;

            double f(double yNew)
            {
                double qOut = RatingCurve != null
                    ? 0.5 * (RatingCurve.Discharge(yOldVal, time) + RatingCurve.Discharge(yNew, time))
                    : 0.0;
                double targetVol = volIn - qOut * duration;
                return NetVolChange(yOldVal, yNew) - targetVol;
            }

            double yTarget = MathNet.Numerics.RootFinding.Brent.FindRoot(f, YMin, YMax, 1e-8);

            if (MinStage.HasValue && yTarget < MinStage.Value)
                yTarget = MinStage.Value;

            return yTarget;
        }

        /// <summary>d(Y_new)/d(vol_in).</summary>
        public double DYNewDVolIn(double duration, double volIn, double yOld, double? time = null)
        {
            double yNew = MassBalance(duration, volIn, yOld, time);
            if (MinStage.HasValue && yNew <= MinStage.Value) return 0.0;
            return 1.0 / AreaAt(yNew);
        }

        /// <summary>Computes total energy loss at the channel-reservoir interface.</summary>
        public double EnergyLoss(double entryArea, double flow, double roughness,
            double hydraulicRadius, double? aStr = null)
        {
            if (!CaptureLosses) return 0;
            double hf = FrictionLoss(entryArea, flow, roughness, hydraulicRadius);
            double hExp = ExpansionLoss(entryArea, flow, aStr);
            double hEmp = EmpiricalLoss(entryArea, flow);
            return hf + hExp + hEmp;
        }

        public double FrictionLoss(double aEnt, double flow, double roughness, double hydraulicRadius)
        {
            double sf = Hydraulics.FrictionSlope(flow, area: aEnt, roughness: roughness,
                hydraulicRadius: hydraulicRadius);
            return sf * (ReservoirLength ?? 0);
        }

        public double ExpansionLoss(double aEnt, double flow, double? aStr = null)
        {
            if (aStr == null) return 0;
            double K = Math.Pow(1.0 - aEnt / aStr.Value, 2);
            double V = flow / aEnt;
            return K * V * V / (2.0 * Hydraulics.G);
        }

        public double EmpiricalLoss(double entryArea, double flow)
        {
            double V = flow / entryArea;
            return KQ * V * V / (2.0 * Hydraulics.G);
        }

        public double DEnergyLossDFlow(double entryArea, double flow, double roughness,
            double hydraulicRadius, double? aStr = null)
        {
            if (!CaptureLosses) return 0;
            return DFrictionLossDFlow(entryArea, flow, roughness, hydraulicRadius)
                   + DExpansionLossDFlow(entryArea, flow, aStr)
                   + DEmpiricalLossDFlow(entryArea, flow);
        }

        public double DEnergyLossDArea(double entryArea, double flow, double roughness,
            double hydraulicRadius, double dRdA, double? aStr = null)
        {
            if (!CaptureLosses) return 0;
            return DFrictionLossDArea(entryArea, flow, roughness, hydraulicRadius, dRdA)
                   + DExpansionLossDArea(entryArea, flow, aStr)
                   + DEmpiricalLossDArea(entryArea, flow);
        }

        private double DFrictionLossDFlow(double aEnt, double flow, double roughness, double hydraulicRadius)
        {
            double dSfdQ = Hydraulics.DFrictionSlopeDFlow(flow, area: aEnt, roughness: roughness,
                hydraulicRadius: hydraulicRadius);
            return dSfdQ * (ReservoirLength ?? 0);
        }

        private double DFrictionLossDArea(double aEnt, double flow, double roughness,
            double hydraulicRadius, double dRdA)
        {
            double dSfdA = Hydraulics.DFrictionSlopeDArea(flow, area: aEnt, roughness: roughness,
                hydraulicRadius: hydraulicRadius, dRdA: dRdA);
            return dSfdA * (ReservoirLength ?? 0);
        }

        private double DExpansionLossDArea(double aEnt, double flow, double? aStr = null)
        {
            if (aStr == null) return 0;
            double K = Math.Pow(1.0 - aEnt / aStr.Value, 2);
            double V = flow / aEnt;
            double dKdA = 2.0 * (1.0 - aEnt / aStr.Value) * (-1.0 / aStr.Value);
            double dVdA = -flow / (aEnt * aEnt);
            return (K * 2.0 * V * dVdA + V * V * dKdA) / (2.0 * Hydraulics.G);
        }

        private double DExpansionLossDFlow(double aEnt, double flow, double? aStr = null)
        {
            if (aStr == null) return 0;
            double K = Math.Pow(1.0 - aEnt / aStr.Value, 2);
            double V = flow / aEnt;
            double dVdQ = 1.0 / aEnt;
            return K * 2.0 * V * dVdQ / (2.0 * Hydraulics.G);
        }

        private double DEmpiricalLossDArea(double aEnt, double flow)
        {
            double V = flow / aEnt;
            double dVdA = -flow / (aEnt * aEnt);
            return KQ * 2.0 * V * dVdA / (2.0 * Hydraulics.G);
        }

        private double DEmpiricalLossDFlow(double aEnt, double flow)
        {
            double V = flow / aEnt;
            double dVdQ = 1.0 / aEnt;
            return KQ * 2.0 * V * dVdQ / (2.0 * Hydraulics.G);
        }

        /// <summary>Sets the stage-area curve for the reservoir.</summary>
        public void SetAreaCurve(double[,] table, double alpha = 1, double beta = 0,
            bool updateSolutionBoundaries = true)
        {
            _alpha = alpha;
            _beta = beta;
            _areaCurve = table;

            int n = table.GetLength(0);
            _areaCurveStages = new double[n];
            _areaCurveAreas = new double[n];
            for (int i = 0; i < n; i++)
            {
                _areaCurveStages[i] = table[i, 0];
                _areaCurveAreas[i] = table[i, 1];
            }

            // Numerical gradient
            _areaGradient = new double[n];
            for (int i = 0; i < n; i++)
            {
                if (i == 0)
                    _areaGradient[i] = (_areaCurveAreas[1] - _areaCurveAreas[0])
                                       / (_areaCurveStages[1] - _areaCurveStages[0]);
                else if (i == n - 1)
                    _areaGradient[i] = (_areaCurveAreas[n - 1] - _areaCurveAreas[n - 2])
                                       / (_areaCurveStages[n - 1] - _areaCurveStages[n - 2]);
                else
                    _areaGradient[i] = (_areaCurveAreas[i + 1] - _areaCurveAreas[i - 1])
                                       / (_areaCurveStages[i + 1] - _areaCurveStages[i - 1]);
            }

            if (updateSolutionBoundaries)
            {
                YMin = _areaCurveStages[0];
                YMax = _areaCurveStages[n - 1];
            }
        }

        /// <summary>Returns the surface area at a given stage.</summary>
        public double AreaAt(double stage)
        {
            if (_areaCurveStages == null || _areaCurveAreas == null)
                return SurfaceArea ?? throw new InvalidOperationException("Area curve not defined.");

            return _alpha * Utility.Interp(stage + _beta, _areaCurveStages, _areaCurveAreas);
        }

        /// <summary>dA/dY at a given stage.</summary>
        public double DAreaDStage(double stage)
        {
            if (_areaCurveStages == null || _areaGradient == null) return 0;
            return _alpha * Utility.Interp(stage, _areaCurveStages, _areaGradient);
        }

        /// <summary>Computes net volume change between two stages.</summary>
        public double NetVolChange(double y1, double y2)
        {
            if (_areaCurveStages == null)
                return (y2 - y1) * (SurfaceArea ?? throw new InvalidOperationException("Surface area not defined."));

            // Trapezoidal integration
            double step = double.MaxValue;
            int n = _areaCurveStages!.Length;
            for (int i = 0; i < n - 1; i++)
            {
                double diff = Math.Abs(_areaCurveStages[i + 1] - _areaCurveStages[i]);
                if (diff < step) step = diff;
            }

            int numSteps = (int)(Math.Abs(y2 - y1) / step);
            if (numSteps > 2)
            {
                double[] ys = Linspace(y1, y2, numSteps);
                double sum = 0;
                for (int i = 0; i < ys.Length - 1; i++)
                    sum += 0.5 * (AreaAt(ys[i]) + AreaAt(ys[i + 1])) * (ys[i + 1] - ys[i]);
                return sum;
            }
            else
            {
                return 0.5 * (AreaAt(y2) + AreaAt(y1)) * (y2 - y1);
            }
        }

        private static double[] Linspace(double start, double end, int num)
        {
            double[] result = new double[num];
            double step = num > 1 ? (end - start) / (num - 1) : 0;
            for (int i = 0; i < num; i++)
                result[i] = start + i * step;
            return result;
        }
    }
}
