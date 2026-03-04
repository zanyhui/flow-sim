using System;

namespace FlowSim.Models
{
    /// <summary>
    /// Static class containing hydraulic calculation functions based on Manning's equation and Saint-Venant theory.
    /// </summary>
    public static class Hydraulics
    {
        public const double G = 9.80665; // gravitational acceleration m/s²

        /// <summary>Compute conveyance K = A * R^(2/3) / n</summary>
        public static double Conveyance(double A, double n, double R)
        {
            return A * Math.Pow(R, 2.0 / 3.0) / n;
        }

        /// <summary>Compute dK/dA</summary>
        public static double DConveyance_DA(double A, double n, double R, double dR_dA)
        {
            return (Math.Pow(R, 2.0 / 3.0) + A * (2.0 / 3.0) * Math.Pow(R, 2.0 / 3.0 - 1.0) * dR_dA) / n;
        }

        /// <summary>Compute friction slope Sf = Q*|Q| / K^2</summary>
        public static double FrictionSlope(double Q, double K)
        {
            return Q * Math.Abs(Q) / (K * K);
        }

        /// <summary>Compute dSf/dA</summary>
        public static double DFrictionSlope_DA(double Q, double K, double dK_dA)
        {
            return -2.0 * FrictionSlope(Q, K) * (dK_dA / K);
        }

        /// <summary>Compute dSf/dQ</summary>
        public static double DFrictionSlope_DQ(double Q, double K)
        {
            return 2.0 * Math.Abs(Q) / (K * K);
        }

        /// <summary>Compute normal flow Q = K * sqrt(|S0|), sign follows S0</summary>
        public static double NormalFlow(double bedSlope, double K)
        {
            double Q = K * Math.Sqrt(Math.Abs(bedSlope));
            if (bedSlope < 0) Q = -Q;
            return Q;
        }

        /// <summary>Compute dQn/dA</summary>
        public static double DNormalFlow_DA(double S0, double dK_dA)
        {
            double dQ = dK_dA * Math.Sqrt(Math.Abs(S0));
            if (S0 < 0) dQ = -dQ;
            return dQ;
        }

        /// <summary>Compute Froude number</summary>
        public static double FroudeNumber(double T, double A, double Q)
        {
            double V = Q / Math.Max(A, 1e-6);
            double D = A / Math.Max(T, 1e-6);
            return V / Math.Sqrt(G * Math.Max(D, 1e-6));
        }

        /// <summary>Compute dFr/dA</summary>
        public static double DFroude_DA(double T, double A, double Q)
        {
            double V = Q / A;
            double D = A / T;
            double dV_dA = -Q / (A * A);
            double dD_dA = 1.0 / T;
            return -0.5 * V * Math.Pow(G * D, -1.5) * G * dD_dA + dV_dA * Math.Pow(G * D, -0.5);
        }

        /// <summary>Compute dFr/dQ</summary>
        public static double DFroude_DQ(double T, double A)
        {
            double D = A / T;
            double dV_dQ = 1.0 / A;
            return dV_dQ * Math.Pow(G * D, -0.5);
        }

        /// <summary>Darcy-Weisbach friction factor f = 8g/C^2 where C = R^(1/6)/n</summary>
        public static double DarcyWeisbachF(double n, double R)
        {
            double C = Math.Pow(R, 1.0 / 6.0) / n;
            return 8.0 * G / (C * C);
        }

        /// <summary>Curvature slope Sc</summary>
        public static double CurvatureSlope(double h, double T, double A, double Q, double n, double R, double rc)
        {
            double Fr = FroudeNumber(T, A, Q);
            double f = DarcyWeisbachF(n, R);
            double numerator = (2.86 * Math.Sqrt(f) + 2.07 * f) * h * h * Fr * Fr;
            double denominator = (0.565 + Math.Sqrt(f)) * rc * rc;
            return numerator / denominator;
        }

        /// <summary>dSc/dA</summary>
        public static double DCurvatureSlope_DA(double h, double A, double Q, double n, double R, double rc, double dR_dA, double T)
        {
            double Fr = FroudeNumber(T, A, Q);
            double C = Math.Pow(R, 1.0 / 6.0) / n;
            double f = 8.0 * G / (C * C);
            double dh_dA = 1.0 / T;
            double dFr_dA = DFroude_DA(T, A, Q);
            double df_dA = -(8.0 / 3.0) * G * n * n * Math.Pow(R, -4.0 / 3.0) * dR_dA;
            double sqrtf = Math.Sqrt(f);
            double num = (2.86 * sqrtf + 2.07 * f) * h * h * Fr * Fr;
            double den = (0.565 + sqrtf) * rc * rc;
            double dnum_dA = (2.86 / (2.0 * sqrtf) * df_dA + 2.07 * df_dA) * h * h * Fr * Fr
                           + (2.86 * sqrtf + 2.07 * f) * (2.0 * h * dh_dA * Fr * Fr + h * h * 2.0 * Fr * dFr_dA);
            double dden_dA = (1.0 / (2.0 * sqrtf) * df_dA) * rc * rc;
            return (dnum_dA * den - num * dden_dA) / (den * den);
        }

        /// <summary>dSc/dQ</summary>
        public static double DCurvatureSlope_DQ(double h, double T, double A, double Q, double n, double R, double rc)
        {
            double Fr = FroudeNumber(T, A, Q);
            double C = Math.Pow(R, 1.0 / 6.0) / n;
            double f = 8.0 * G / (C * C);
            double dFr_dQ = DFroude_DQ(T, A);
            double sqrtf = Math.Sqrt(f);
            double den = (0.565 + sqrtf) * rc * rc;
            double dnum_dQ = (2.86 * sqrtf + 2.07 * f) * h * h * 2.0 * Fr * dFr_dQ;
            return dnum_dQ / den;
        }

        /// <summary>Brent's method for finding root of f(x)=0 in [a,b]</summary>
        public static double Brentq(Func<double, double> f, double a, double b, double tol = 1e-8, int maxIter = 100)
        {
            double fa = f(a);
            double fb = f(b);
            if (fa * fb > 0)
                throw new ArgumentException($"f(a) and f(b) must have opposite signs. f({a})={fa}, f({b})={fb}");
            if (Math.Abs(fa) < tol) return a;
            if (Math.Abs(fb) < tol) return b;
            double c = a, fc = fa;
            double s = 0, fs = 0;
            double d = 0;
            bool mflag = true;
            for (int iter = 0; iter < maxIter; iter++)
            {
                if (Math.Abs(fa - fc) > 1e-15 && Math.Abs(fb - fc) > 1e-15)
                    s = a * fb * fc / ((fa - fb) * (fa - fc))
                      + b * fa * fc / ((fb - fa) * (fb - fc))
                      + c * fa * fb / ((fc - fa) * (fc - fb));
                else
                    s = b - fb * (b - a) / (fb - fa);

                bool cond1 = !((3.0 * a + b) / 4.0 < s && s < b || b < s && s < (3.0 * a + b) / 4.0);
                bool cond2 = mflag && Math.Abs(s - b) >= Math.Abs(b - c) / 2.0;
                bool cond3 = !mflag && Math.Abs(s - b) >= Math.Abs(c - d) / 2.0;
                bool cond4 = mflag && Math.Abs(b - c) < tol;
                bool cond5 = !mflag && Math.Abs(c - d) < tol;

                if (cond1 || cond2 || cond3 || cond4 || cond5)
                { s = (a + b) / 2.0; mflag = true; }
                else mflag = false;

                fs = f(s);
                d = c; c = b; fc = fb;
                if (fa * fs < 0) { b = s; fb = fs; }
                else { a = s; fa = fs; }
                if (Math.Abs(fa) < Math.Abs(fb))
                {
                    double tmp = a; a = b; b = tmp;
                    double ftmp = fa; fa = fb; fb = ftmp;
                }
                if (Math.Abs(fb) < tol || Math.Abs(b - a) < tol) return b;
            }
            return b;
        }

        /// <summary>Linear interpolation</summary>
        public static double Interp(double x, double[] xs, double[] ys)
        {
            if (xs.Length == 0) throw new ArgumentException("Empty array");
            if (x <= xs[0]) return ys[0];
            if (x >= xs[xs.Length - 1]) return ys[xs.Length - 1];
            int i = Array.BinarySearch(xs, x);
            if (i >= 0) return ys[i];
            i = ~i;
            double t = (x - xs[i - 1]) / (xs[i] - xs[i - 1]);
            return ys[i - 1] + t * (ys[i] - ys[i - 1]);
        }

        /// <summary>Numerical gradient (central differences)</summary>
        public static double[] Gradient(double[] y, double[] x)
        {
            int n = y.Length;
            double[] grad = new double[n];
            if (n == 1) { grad[0] = 0; return grad; }
            grad[0] = (y[1] - y[0]) / (x[1] - x[0]);
            grad[n - 1] = (y[n - 1] - y[n - 2]) / (x[n - 1] - x[n - 2]);
            for (int i = 1; i < n - 1; i++)
                grad[i] = (y[i + 1] - y[i - 1]) / (x[i + 1] - x[i - 1]);
            return grad;
        }

        /// <summary>Trapezoidal integration</summary>
        public static double Trapezoid(double[] y, double[] x)
        {
            double sum = 0;
            for (int i = 0; i < x.Length - 1; i++)
                sum += 0.5 * (y[i] + y[i + 1]) * (x[i + 1] - x[i]);
            return sum;
        }
    }
}
