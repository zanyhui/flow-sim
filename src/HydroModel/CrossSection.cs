using System;
using MathNet.Numerics;

namespace HydroModel
{
    /// <summary>
    /// Abstract base class for hydraulic cross-sections.
    /// Equivalent to the CrossSection ABC in cross_section.py.
    /// </summary>
    public abstract class CrossSection
    {
        public double NLeft { get; set; }
        public double NMain { get; set; }
        public double NRight { get; set; }
        public double LeftFloodplainLimit { get; set; }
        public double RightFloodplainLimit { get; set; }
        public double Curvature { get; set; }
        public double? BedSlope { get; set; }

        // Caching fields
        protected double? _lastHw;
        protected (double A, double P, double R, double T)? _lastRes;
        protected double? _lastHwN;
        protected double? _lastN;

        protected CrossSection(double? n = null, double? bedSlope = null, double curvature = 0.0)
        {
            NLeft = n ?? 0;
            NMain = n ?? 0;
            NRight = n ?? 0;
            LeftFloodplainLimit = 0.0;
            RightFloodplainLimit = 0.0;
            Curvature = curvature;
            BedSlope = bedSlope;
        }

        // -----------------------------------------------------------------
        // Abstract properties
        // -----------------------------------------------------------------

        /// <summary>Lowest elevation of the cross-section bed.</summary>
        public abstract double ZMin { get; }

        /// <summary>Total width of the cross-section.</summary>
        public abstract double Width { get; }

        // -----------------------------------------------------------------
        // Abstract methods
        // -----------------------------------------------------------------

        /// <summary>Returns (A, P, R, T) for a given water surface elevation.</summary>
        public abstract (double A, double P, double R, double T) Properties(double hw);

        /// <summary>Compute equivalent Manning's n at hw.</summary>
        public abstract double GetEquivalentN(double hw);

        /// <summary>Compute conveyance K at hw.</summary>
        public abstract double Conveyance(double hw);

        /// <summary>Compute dK/dA at hw.</summary>
        public abstract double DConveyanceDArea(double hw);

        /// <summary>Compute dR/dA at hw.</summary>
        public abstract double DHydraulicRadiusDArea(double hw);

        /// <summary>Compute dA/dh at hw (= top width).</summary>
        public abstract double DAreaDDepth(double hw);

        /// <summary>Return the bed elevation at lateral coordinate x.</summary>
        public abstract double ZAt(double x);

        // -----------------------------------------------------------------
        // Concrete helper methods
        // -----------------------------------------------------------------

        public double Area(double hw) => Properties(hw).A;
        public double WettedPerimeter(double hw) => Properties(hw).P;
        public double HydraulicRadius(double hw) => Properties(hw).R;
        public double TopWidth(double hw) => Properties(hw).T;

        public (double nLeft, double nMain, double nRight, double leftFpLimit, double rightFpLimit)
            GetRoughnessParameters()
        {
            return (NLeft, NMain, NRight, LeftFloodplainLimit, RightFloodplainLimit);
        }

        public void SetRoughnessParameters(double nLeft, double nMain, double nRight,
            double leftFpLimit, double rightFpLimit)
        {
            NLeft = nLeft;
            NMain = nMain;
            NRight = nRight;
            LeftFloodplainLimit = leftFpLimit;
            RightFloodplainLimit = rightFpLimit;
        }

        /// <summary>Computes friction slope (Sf).</summary>
        public virtual double FrictionSlope(double depth, double flow)
        {
            double hw = depth + ZMin;
            double K = Conveyance(hw);
            return Hydraulics.FrictionSlope(flow, k: K);
        }

        /// <summary>Computes dSf/dA.</summary>
        public virtual double DFrictionSlopeDArea(double depth, double flow)
        {
            double hw = depth + ZMin;
            double K = Conveyance(hw);
            double dK = DConveyanceDArea(hw);
            return Hydraulics.DFrictionSlopeDArea(flow, k: K, dKdA: dK);
        }

        /// <summary>Computes dSf/dQ.</summary>
        public virtual double DFrictionSlopeDFlow(double depth, double flow)
        {
            double hw = depth + ZMin;
            double K = Conveyance(hw);
            return Hydraulics.DFrictionSlopeDFlow(flow, k: K);
        }

        /// <summary>Computes curvature slope (Sc).</summary>
        public virtual double CurvatureSlope(double depth, double flow)
        {
            if (Curvature == 0) return 0.0;
            double hw = depth + ZMin;
            double n = GetEquivalentN(hw);
            var (A, P, R, T) = Properties(hw);
            return Hydraulics.CurvatureSlope(depth, T, A, flow, n, R, 1.0 / Curvature);
        }

        /// <summary>Computes dSc/dA.</summary>
        public virtual double DCurvatureSlopeDArea(double depth, double flow)
        {
            if (Math.Abs(Curvature) <= 1e-12) return 0.0;
            double hw = depth + ZMin;
            double n = GetEquivalentN(hw);
            var (A, P, R, T) = Properties(hw);
            double dRdA = DHydraulicRadiusDArea(hw);
            return Hydraulics.DCurvatureSlopeDArea(depth, A, flow, n, R, 1.0 / Curvature, dRdA, T)
                   * DAreaDDepth(hw);
        }

        /// <summary>Computes dSc/dQ.</summary>
        public virtual double DCurvatureSlopeDFlow(double depth, double flow)
        {
            if (Math.Abs(Curvature) <= 1e-12) return 0.0;
            double hw = depth + ZMin;
            double n = GetEquivalentN(hw);
            var (A, P, R, T) = Properties(hw);
            return Hydraulics.DCurvatureSlopeDFlow(depth, T, A, flow, n, R, 1.0 / Curvature);
        }

        /// <summary>Computes the normal flow rate for a given water level.</summary>
        public double NormalFlow(double hw)
        {
            if (BedSlope == null || BedSlope <= 0.0) return 0.0;
            double K = Conveyance(hw);
            return Hydraulics.NormalFlow(BedSlope.Value, k: K);
        }

        /// <summary>Computes the normal flow depth for a target flow rate using Brent's method.</summary>
        public double NormalDepth(double targetFlow, double? hwMax = null)
        {
            double zMin = ZMin;
            double hwMaxVal = hwMax ?? zMin + 100.0;

            double f(double hw) => targetFlow - NormalFlow(hw);

            try
            {
                double hw = MathNet.Numerics.RootFinding.Brent.FindRoot(f, zMin, hwMaxVal, 1e-8);
                return hw - zMin;
            }
            catch
            {
                if (f(zMin) < 0) return 0.0;
                if (f(hwMaxVal) > 0) return hwMaxVal - zMin;
                return 0.0;
            }
        }
    }
}
