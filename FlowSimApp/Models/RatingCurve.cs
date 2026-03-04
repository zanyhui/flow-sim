using System;
using System.Linq;

namespace FlowSim.Models
{
    public enum RatingCurveType { Polynomial, Power }

    /// <summary>Stage-discharge relationship.</summary>
    public class RatingCurve
    {
        public bool Defined { get; private set; }
        public RatingCurveType Type { get; private set; }
        public double A { get; private set; }
        public double B { get; private set; }
        public double C { get; private set; }
        public double StageShift { get; private set; }
        private Func<double, double>? _function;
        private Func<double, double>? _derivative;

        public void Set(RatingCurveType type, double a, double b, double c = 0, double stageShift = 0)
        {
            Type = type;
            A = a; B = b; C = c;
            StageShift = stageShift;
            _function = null; _derivative = null;
            Defined = true;
        }

        public double Discharge(double stage, double time = 0)
        {
            if (!Defined) throw new InvalidOperationException("Rating curve is undefined.");
            if (_function != null) return _function(stage);
            double x = stage + StageShift;
            return Type == RatingCurveType.Polynomial
                ? A * x * x + B * x + C
                : A * Math.Pow(x, B);
        }

        public double DQ_Dz(double stage, double time = 0)
        {
            if (!Defined) throw new InvalidOperationException("Rating curve is undefined.");
            if (_derivative != null) return _derivative(stage);
            double Y = stage + StageShift;
            return Type == RatingCurveType.Polynomial
                ? A * 2.0 * Y + B
                : A * B * Math.Pow(Y, B - 1.0);
        }

        public double Stage(double discharge, double trialStage = double.NaN, double tol = 1e-2)
        {
            if (!Defined) throw new InvalidOperationException("Rating curve is undefined.");
            // Start slightly above the stage-shift datum to avoid zero/negative evaluation
            if (double.IsNaN(trialStage)) trialStage = -StageShift * 1.05;
            double q = Discharge(trialStage);
            for (int i = 0; i < 100 && Math.Abs(q - discharge) > tol; i++)
            {
                double d = DQ_Dz(trialStage);
                if (Math.Abs(d) < 1e-12) break;
                trialStage -= (q - discharge) / d;
                q = Discharge(trialStage);
            }
            return trialStage;
        }

        public void Fit(double[] discharges, double[] stages, RatingCurveType type = RatingCurveType.Polynomial, double stageShift = 0)
        {
            if (discharges.Length < 3) throw new ArgumentException("Need at least 3 points.");
            Type = type;
            StageShift = stageShift;
            double[] shifted = stages.Select(s => s + stageShift).ToArray();

            if (type == RatingCurveType.Polynomial)
            {
                double s0 = shifted.Length, s1 = shifted.Sum(), s2 = shifted.Sum(x => x * x);
                double s3 = shifted.Sum(x => x * x * x), s4 = shifted.Sum(x => x * x * x * x);
                double t0 = discharges.Sum(), t1 = 0, t2 = 0;
                for (int i = 0; i < shifted.Length; i++) { t1 += discharges[i] * shifted[i]; t2 += discharges[i] * shifted[i] * shifted[i]; }
                double[,] M = { { s4, s3, s2 }, { s3, s2, s1 }, { s2, s1, s0 } };
                double[] rhs = { t2, t1, t0 };
                double[] sol = SolveLinear3x3(M, rhs);
                A = sol[0]; B = sol[1]; C = sol[2];
            }
            else
            {
                double[] logY = shifted.Select(y => Math.Log(y)).ToArray();
                double[] logQ = discharges.Select(q => Math.Log(q)).ToArray();
                double meanX = logY.Average(), meanY = logQ.Average();
                double num = 0, den = 0;
                for (int i = 0; i < logY.Length; i++) { num += (logY[i] - meanX) * (logQ[i] - meanY); den += (logY[i] - meanX) * (logY[i] - meanX); }
                B = den > 0 ? num / den : 1;
                A = Math.Exp(meanY - B * meanX);
            }
            Defined = true;
        }

        private static double[] SolveLinear3x3(double[,] M, double[] rhs)
        {
            double[,] a = (double[,])M.Clone();
            double[] b = (double[])rhs.Clone();
            const double epsilon = 1e-12;
            int n = 3;
            for (int k = 0; k < n; k++)
            {
                int pivot = k;
                for (int i = k + 1; i < n; i++)
                    if (Math.Abs(a[i, k]) > Math.Abs(a[pivot, k])) pivot = i;
                for (int j = 0; j < n; j++) { double t = a[k, j]; a[k, j] = a[pivot, j]; a[pivot, j] = t; }
                { double t = b[k]; b[k] = b[pivot]; b[pivot] = t; }
                if (Math.Abs(a[k, k]) < epsilon)
                    throw new InvalidOperationException("Singular matrix in rating curve polynomial fit.");
                for (int i = k + 1; i < n; i++)
                {
                    double f = a[i, k] / a[k, k];
                    for (int j = k; j < n; j++) a[i, j] -= f * a[k, j];
                    b[i] -= f * b[k];
                }
            }
            double[] x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                x[i] = b[i];
                for (int j = i + 1; j < n; j++) x[i] -= a[i, j] * x[j];
                if (Math.Abs(a[i, i]) < epsilon)
                    throw new InvalidOperationException("Singular matrix in rating curve polynomial fit.");
                x[i] /= a[i, i];
            }
            return x;
        }
    }
}
