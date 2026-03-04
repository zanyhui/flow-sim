using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowSim.Models
{
    /// <summary>0D lumped storage (reservoir) element.</summary>
    public class LumpedStorage
    {
        public RatingCurve? RatingCurve { get; set; }
        public double? SurfaceArea { get; set; }
        public double? MinStage { get; set; }
        public List<double[]> StageHydrograph { get; } = new();
        public double[,]? AreaCurve { get; private set; }
        public bool CaptureLosses { get; set; } = false;
        public double Cc { get; set; } = 0.5;
        public double KQ { get; set; } = 0;
        public double? ReservoirLength { get; set; }

        private double _yMin, _yMax;
        private double _alpha = 1, _beta = 0;
        private double[]? _areaCurveStages;
        private double[]? _areaCurveAreas;

        public LumpedStorage(double yMin, double yMax, double? surfaceArea = null,
                              double? minStage = null, RatingCurve? ratingCurve = null)
        {
            _yMin = yMin; _yMax = yMax;
            SurfaceArea = surfaceArea;
            MinStage = minStage;
            RatingCurve = ratingCurve;
        }

        public double MassBalance(double duration, double volIn, double yOld, double time = 0)
        {
            double F(double yNew)
            {
                double qOut = RatingCurve != null
                    ? 0.5 * (RatingCurve.Discharge(yOld, time) + RatingCurve.Discharge(yNew, time))
                    : 0.0;
                double targetVol = volIn - qOut * duration;
                return NetVolChange(yOld, yNew) - targetVol;
            }
            double yTarget = Hydraulics.Brentq(F, _yMin, _yMax);
            if (MinStage.HasValue && yTarget < MinStage.Value) yTarget = MinStage.Value;
            return yTarget;
        }

        public double DYnew_DvolIn(double duration, double volIn, double yOld, double time = 0)
        {
            double yNew = MassBalance(duration, volIn, yOld, time);
            if (MinStage.HasValue && yNew <= MinStage.Value) return 0.0;
            return 1.0 / AreaAt(yNew);
        }

        public double EnergyLoss(double entryArea, double flow, double roughness, double hydraulicRadius, double? aStr = null)
        {
            if (!CaptureLosses) return 0.0;
            double hf = FrictionLoss(entryArea, flow, roughness, hydraulicRadius);
            double hExp = ExpansionLoss(entryArea, flow, aStr);
            double hEmp = EmpiricalLoss(entryArea, flow);
            return hf + hExp + hEmp;
        }

        private double FrictionLoss(double aEnt, double Q, double n, double R)
            => Hydraulics.FrictionSlope(Q, Hydraulics.Conveyance(aEnt, n, R)) * (ReservoirLength ?? 0);

        private double ExpansionLoss(double aEnt, double Q, double? aStr)
        {
            if (!aStr.HasValue) return 0;
            double K = Math.Pow(1.0 - aEnt / aStr.Value, 2.0);
            double V = Q / aEnt;
            return K * V * V / (2 * Hydraulics.G);
        }

        private double EmpiricalLoss(double aEnt, double Q)
        {
            double V = Q / aEnt;
            return KQ * V * V / (2 * Hydraulics.G);
        }

        public double Dhl_DA(double entryArea, double flow, double roughness, double hydraulicRadius, double dR_dA, double? aStr = null)
        {
            if (!CaptureLosses) return 0.0;
            double K = Hydraulics.Conveyance(entryArea, roughness, hydraulicRadius);
            double dK = Hydraulics.DConveyance_DA(entryArea, roughness, hydraulicRadius, dR_dA);
            double dhf_dA = Hydraulics.DFrictionSlope_DA(flow, K, dK) * (ReservoirLength ?? 0);
            double V = flow / entryArea;
            double dV_dA = -flow / (entryArea * entryArea);
            double dhEmp_dA = KQ * 2 * V * dV_dA / (2 * Hydraulics.G);
            double dhExp_dA = 0;
            if (aStr.HasValue)
            {
                double Kc = Math.Pow(1.0 - entryArea / aStr.Value, 2.0);
                double dKc_dA = 2.0 * (1.0 - entryArea / aStr.Value) * (-1.0 / aStr.Value);
                dhExp_dA = (Kc * 2 * V * dV_dA + V * V * dKc_dA) / (2 * Hydraulics.G);
            }
            return dhf_dA + dhExp_dA + dhEmp_dA;
        }

        public double Dhl_DQ(double entryArea, double flow, double roughness, double hydraulicRadius, double? aStr = null)
        {
            if (!CaptureLosses) return 0.0;
            double K = Hydraulics.Conveyance(entryArea, roughness, hydraulicRadius);
            double dhf_dQ = Hydraulics.DFrictionSlope_DQ(flow, K) * (ReservoirLength ?? 0);
            double V = flow / entryArea;
            double dV_dQ = 1.0 / entryArea;
            double dhEmp_dQ = KQ * 2 * V * dV_dQ / (2 * Hydraulics.G);
            double dhExp_dQ = 0;
            if (aStr.HasValue)
            {
                double Kc = Math.Pow(1.0 - entryArea / aStr.Value, 2.0);
                dhExp_dQ = Kc * 2 * V * dV_dQ / (2 * Hydraulics.G);
            }
            return dhf_dQ + dhExp_dQ + dhEmp_dQ;
        }

        public void SetAreaCurve(double[,] table, double alpha = 1, double beta = 0, bool updateBoundaries = true)
        {
            AreaCurve = table;
            _alpha = alpha; _beta = beta;
            int n = table.GetLength(0);
            _areaCurveStages = new double[n];
            _areaCurveAreas = new double[n];
            for (int i = 0; i < n; i++) { _areaCurveStages[i] = table[i, 0]; _areaCurveAreas[i] = table[i, 1]; }
            if (updateBoundaries) { _yMin = _areaCurveStages[0]; _yMax = _areaCurveStages[n - 1]; }
        }

        public double AreaAt(double stage)
        {
            if (AreaCurve == null) return SurfaceArea ?? 0;
            return _alpha * Hydraulics.Interp(stage + _beta, _areaCurveStages!, _areaCurveAreas!);
        }

        public double NetVolChange(double y1, double y2)
        {
            if (AreaCurve == null) return (y2 - y1) * (SurfaceArea ?? 0);
            int n = Math.Max(3, (int)(Math.Abs(y2 - y1) / 0.01) + 2);
            double[] ys = Enumerable.Range(0, n).Select(i => y1 + (y2 - y1) * i / (n - 1)).ToArray();
            double[] areas = ys.Select(AreaAt).ToArray();
            return Hydraulics.Trapezoid(areas, ys);
        }
    }
}
