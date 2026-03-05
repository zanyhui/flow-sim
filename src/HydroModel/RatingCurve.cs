using System;

namespace HydroModel
{
    /// <summary>
    /// Stage-discharge relationship (rating curve).
    /// Equivalent to rating_curve.py.
    /// </summary>
    public class RatingCurve
    {
        private Func<double, double>? _function;
        private Func<double, double>? _derivative;

        private double _a, _b, _c;
        private double _stageShift;

        /// <summary>Whether the rating curve has been defined.</summary>
        public bool Defined { get; private set; }

        /// <summary>Type of rating curve: "polynomial" or "power".</summary>
        public string? Type { get; private set; }

        public RatingCurve()
        {
            Defined = false;
            _stageShift = 0;
        }

        /// <summary>
        /// Sets the rating curve using pre-defined coefficients.
        /// </summary>
        /// <param name="type">"polynomial" or "power".</param>
        /// <param name="a">Coefficient a.</param>
        /// <param name="b">Coefficient b.</param>
        /// <param name="c">Coefficient c (polynomial only).</param>
        /// <param name="stageShift">Stage offset.</param>
        public void Set(string type, double a, double b, double? c = null, double? stageShift = null)
        {
            _stageShift = stageShift ?? 0;

            if (type == "polynomial")
            {
                if (c == null)
                    throw new ArgumentException("c must be specified for polynomial type.");
                _a = a; _b = b; _c = c.Value;
            }
            else if (type == "power")
            {
                _a = a; _b = b;
            }
            else
            {
                throw new ArgumentException("Invalid type. Use 'polynomial' or 'power'.");
            }

            _function = null;
            _derivative = null;
            Defined = true;
            Type = type;
        }

        /// <summary>
        /// Computes discharge for a given stage.
        /// </summary>
        public double Discharge(double stage, double? time = null)
        {
            if (!Defined)
                throw new InvalidOperationException("Rating curve is undefined.");

            if (_function != null)
                return _function(stage);

            double x = stage + _stageShift;
            if (Type == "polynomial")
                return _a * x * x + _b * x + _c;
            else
                return _a * Math.Pow(x, _b);
        }

        /// <summary>
        /// Computes the stage for a given discharge using Newton-Raphson iteration.
        /// </summary>
        public double Stage(double discharge, double? trialStage = null, double? time = null,
            double tolerance = 1e-2, double rate = 1)
        {
            if (!Defined)
                throw new InvalidOperationException("Rating curve is undefined.");

            double ts = trialStage ?? -_stageShift * 1.05;
            double q = Discharge(ts, time);

            while (Math.Abs(q - discharge) > tolerance)
            {
                double func = q - discharge;
                double deriv = DischargeDStage(ts, time);
                ts -= rate * func / deriv;
                q = Discharge(ts, time);
            }

            return ts;
        }

        /// <summary>
        /// Fits the rating curve from discharge-stage data.
        /// </summary>
        public void Fit(double[] discharges, double[] stages, double stageShift = 0,
            string type = "polynomial", int degree = 2)
        {
            if (discharges.Length < 3)
                throw new ArgumentException("Need at least 3 points.");
            if (discharges.Length != stages.Length)
                throw new ArgumentException("Q and Y lists should have the same lengths.");

            Type = type;
            _stageShift = stageShift;

            double[] shiftedStages = new double[stages.Length];
            for (int i = 0; i < stages.Length; i++)
                shiftedStages[i] = stages[i] + _stageShift;

            foreach (double ss in shiftedStages)
                if (ss <= 0)
                    throw new ArgumentException("All (stage - base) values must be positive.");

            if (type == "polynomial")
            {
                // Fit quadratic using least squares
                FitPolynomial(discharges, shiftedStages, degree);
            }
            else if (type == "power")
            {
                // Fit power law in log-log space
                FitPowerLaw(discharges, shiftedStages);
            }
            else
            {
                throw new ArgumentException("Invalid rating curve type.");
            }

            _function = null;
            _derivative = null;
            Defined = true;
        }

        private void FitPolynomial(double[] y, double[] x, int degree)
        {
            // Simple quadratic least squares: a*x^2 + b*x + c
            // Use MathNet polynomial fit
            double[] fitCoeffs = MathNet.Numerics.Fit.Polynomial(x, y, degree);
            // MathNet returns in ascending order: c[0] + c[1]*x + c[2]*x^2
            if (degree == 2 && fitCoeffs.Length >= 3)
            {
                _c = fitCoeffs[0];
                _b = fitCoeffs[1];
                _a = fitCoeffs[2];
            }
            else
            {
                throw new NotSupportedException("Only degree-2 polynomial fitting is supported without custom functions.");
            }
        }

        private void FitPowerLaw(double[] discharges, double[] shiftedStages)
        {
            // log(Q) = b * log(Y) + log(a)
            double[] logY = new double[shiftedStages.Length];
            double[] logQ = new double[discharges.Length];
            for (int i = 0; i < shiftedStages.Length; i++)
            {
                logY[i] = Math.Log(shiftedStages[i]);
                logQ[i] = Math.Log(discharges[i]);
            }

            double[] fitCoeffs = MathNet.Numerics.Fit.Polynomial(logY, logQ, 1);
            _b = fitCoeffs[1];
            _a = Math.Exp(fitCoeffs[0]);
        }

        /// <summary>
        /// Derivative of discharge w.r.t. stage: dQ/dz.
        /// </summary>
        public double DischargeDStage(double stage, double? time = null)
        {
            double y = stage + _stageShift;

            if (!Defined)
                throw new InvalidOperationException("Rating curve is undefined.");

            if (_derivative != null)
                return _derivative(stage);

            if (Type == "polynomial")
                return _a * 2.0 * y + _b;
            else
                return _a * _b * Math.Pow(y, _b - 1.0);
        }

        /// <summary>Returns a string representation of the rating curve equation.</summary>
        public override string ToString()
        {
            if (!Defined)
                throw new InvalidOperationException("Rating curve is undefined.");

            if (Type == "polynomial")
                return $"{_a} (Y+{_stageShift})^2 + {_b} (Y+{_stageShift}) + {_c}";
            else
                return $"{_a} (Y+{_stageShift})^{_b}";
        }
    }
}
