using System;

namespace HydroModel
{
    /// <summary>
    /// Static class containing hydraulic calculations.
    /// Equivalent to hydraulics.py.
    /// </summary>
    public static class Hydraulics
    {
        /// <summary>Acceleration due to gravity (m/s^2).</summary>
        public const double G = 9.80665;

        /// <summary>
        /// Computes the normal flow rate using Manning's equation.
        /// </summary>
        public static double NormalFlow(double bedSlope, double area = 0, double roughness = 0,
            double hydraulicRadius = 0, double? k = null)
        {
            double K = k ?? Conveyance(area, roughness, hydraulicRadius);
            double Q = K * Math.Pow(Math.Abs(bedSlope), 0.5);
            if (bedSlope < 0) Q = -Q;
            return Q;
        }

        /// <summary>
        /// Computes conveyance K = A * R^(2/3) / n.
        /// </summary>
        public static double Conveyance(double area, double roughness, double hydraulicRadius)
        {
            return area * Math.Pow(hydraulicRadius, 2.0 / 3.0) / roughness;
        }

        /// <summary>
        /// Derivative of conveyance w.r.t. flow area: dK/dA.
        /// </summary>
        public static double DConveyanceDArea(double area, double roughness, double hydraulicRadius,
            double dRdA)
        {
            return (Math.Pow(hydraulicRadius, 2.0 / 3.0)
                    + area * (2.0 / 3.0) * Math.Pow(hydraulicRadius, 2.0 / 3.0 - 1.0) * dRdA)
                   / roughness;
        }

        /// <summary>
        /// Computes friction slope using Manning's equation: Sf = Q*|Q| / K^2.
        /// </summary>
        public static double FrictionSlope(double flow, double area = 0, double roughness = 0,
            double hydraulicRadius = 0, double? k = null)
        {
            double K = k ?? Conveyance(area, roughness, hydraulicRadius);
            return flow * Math.Abs(flow) / (K * K);
        }

        /// <summary>
        /// Partial derivative of Sf w.r.t. flow area A: dSf/dA.
        /// </summary>
        public static double DFrictionSlopeDArea(double flow, double area = 0, double roughness = 0,
            double hydraulicRadius = 0, double dRdA = 0, double? k = null, double? dKdA = null)
        {
            double K = k ?? Conveyance(area, roughness, hydraulicRadius);
            double dK = dKdA ?? DConveyanceDArea(area, roughness, hydraulicRadius, dRdA);
            return -2.0 * FrictionSlope(flow, k: K) * (dK / K);
        }

        /// <summary>
        /// Partial derivative of Sf w.r.t. flow rate Q: dSf/dQ.
        /// </summary>
        public static double DFrictionSlopeDFlow(double flow, double area = 0, double roughness = 0,
            double hydraulicRadius = 0, double? k = null)
        {
            double K = k ?? Conveyance(area, roughness, hydraulicRadius);
            return 2.0 * Math.Abs(flow) / (K * K);
        }

        /// <summary>
        /// Computes the energy gradient due to transverse circulation (Sc).
        /// </summary>
        public static double CurvatureSlope(double depth, double topWidth, double area, double flow,
            double roughness, double hydraulicRadius, double radiusOfCurvature)
        {
            double Fr = FroudeNumber(topWidth, area, flow);
            double f = DarcyWeisbachF(roughness, hydraulicRadius);
            double sqrtF = Math.Sqrt(f);
            double numerator = (2.86 * sqrtF + 2.07 * f) * depth * depth * Fr * Fr;
            double denominator = (0.565 + sqrtF) * radiusOfCurvature * radiusOfCurvature;
            return numerator / denominator;
        }

        /// <summary>
        /// Derivative of curvature slope w.r.t. flow area: dSc/dA.
        /// </summary>
        public static double DCurvatureSlopeDArea(double depth, double area, double flow,
            double roughness, double hydraulicRadius, double radiusOfCurvature, double dRdA, double topWidth)
        {
            double Fr = FroudeNumber(topWidth, area, flow);
            double C = Math.Pow(hydraulicRadius, 1.0 / 6.0) / roughness;
            double f = 8.0 * G / (C * C);
            double sqrtF = Math.Sqrt(f);

            double dhDa = 1.0 / topWidth;
            double dFrDa = DFroudeNumberDArea(topWidth, area, flow);
            double dfDa = -(8.0 / 3.0) * G * roughness * roughness
                          * Math.Pow(hydraulicRadius, -4.0 / 3.0) * dRdA;

            double num = (2.86 * sqrtF + 2.07 * f) * depth * depth * Fr * Fr;
            double den = (0.565 + sqrtF) * radiusOfCurvature * radiusOfCurvature;

            double dNumDA = (2.86 / (2.0 * sqrtF) * dfDa + 2.07 * dfDa) * depth * depth * Fr * Fr
                          + (2.86 * sqrtF + 2.07 * f)
                            * (2.0 * depth * dhDa * Fr * Fr + depth * depth * 2.0 * Fr * dFrDa);
            double dDenDA = (1.0 / (2.0 * sqrtF) * dfDa) * radiusOfCurvature * radiusOfCurvature;

            return (dNumDA * den - num * dDenDA) / (den * den);
        }

        /// <summary>
        /// Derivative of curvature slope w.r.t. flow rate: dSc/dQ.
        /// </summary>
        public static double DCurvatureSlopeDFlow(double depth, double topWidth, double area, double flow,
            double roughness, double hydraulicRadius, double radiusOfCurvature)
        {
            double Fr = FroudeNumber(topWidth, area, flow);
            double C = Math.Pow(hydraulicRadius, 1.0 / 6.0) / roughness;
            double f = 8.0 * G / (C * C);
            double sqrtF = Math.Sqrt(f);

            double dFrDQ = DFroudeNumberDFlow(topWidth, area);
            double num = (2.86 * sqrtF + 2.07 * f) * depth * depth * Fr * Fr;
            double den = (0.565 + sqrtF) * radiusOfCurvature * radiusOfCurvature;
            double dNumDQ = (2.86 * sqrtF + 2.07 * f) * depth * depth * 2.0 * Fr * dFrDQ;

            return (dNumDQ * den - num * 0.0) / (den * den);
        }

        /// <summary>
        /// Computes the Froude number.
        /// </summary>
        public static double FroudeNumber(double topWidth, double area, double flow)
        {
            double V = flow / Math.Max(area, 1e-6);
            double D = area / Math.Max(topWidth, 1e-6);
            return V / Math.Sqrt(G * Math.Max(D, 1e-6));
        }

        /// <summary>
        /// Derivative of Froude number w.r.t. flow area: dFr/dA.
        /// </summary>
        public static double DFroudeNumberDArea(double topWidth, double area, double flow)
        {
            double V = flow / area;
            double D = area / topWidth;
            double dVdA = -flow / (area * area);
            double dDdA = 1.0 / topWidth;
            return -0.5 * V * Math.Pow(G * D, -1.5) * G * dDdA
                   + dVdA * Math.Pow(G * D, -0.5);
        }

        /// <summary>
        /// Derivative of Froude number w.r.t. flow rate: dFr/dQ.
        /// </summary>
        public static double DFroudeNumberDFlow(double topWidth, double area)
        {
            double D = area / topWidth;
            double dVdQ = 1.0 / area;
            return dVdQ * Math.Pow(G * D, -0.5);
        }

        /// <summary>
        /// Derivative of normal flow w.r.t. flow area: dQn/dA.
        /// </summary>
        public static double DNormalFlowDArea(double bedSlope, double area = 0, double roughness = 0,
            double hydraulicRadius = 0, double dRdA = 0, double? dKdA = null)
        {
            double dK = dKdA ?? DConveyanceDArea(area, roughness, hydraulicRadius, dRdA);
            double dQdA = dK * Math.Pow(Math.Abs(bedSlope), 0.5);
            if (bedSlope < 0) dQdA = -dQdA;
            return dQdA;
        }

        /// <summary>
        /// Computes the Darcy-Weisbach friction factor.
        /// </summary>
        public static double DarcyWeisbachF(double roughness, double hydraulicRadius)
        {
            double C = Math.Pow(hydraulicRadius, 1.0 / 6.0) / roughness;
            return 8.0 * G / (C * C);
        }
    }
}
