using System;

namespace HydroModel
{
    /// <summary>
    /// Static utility methods.
    /// Equivalent to utility.py.
    /// </summary>
    public static class Utility
    {
        /// <summary>
        /// Creates a directory if it does not already exist.
        /// </summary>
        public static void CreateDirectoryIfNotExists(string directory)
        {
            if (!System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);
        }

        /// <summary>
        /// Computes the Manhattan (L1) norm of a vector.
        /// </summary>
        public static double ManhattanNorm(double[] vector)
        {
            double sum = 0;
            foreach (double v in vector)
                sum += Math.Abs(v);
            return sum;
        }

        /// <summary>
        /// Computes the Euclidean (L2) norm of a vector.
        /// </summary>
        public static double EuclideanNorm(double[] vector)
        {
            double sum = 0;
            foreach (double v in vector)
                sum += v * v;
            return Math.Sqrt(sum);
        }

        /// <summary>
        /// Converts a duration in seconds to h:mm:ss string.
        /// </summary>
        public static string SecondsToHms(int seconds)
        {
            if (seconds < 0) return "0:00:00";
            int h = seconds / 3600;
            int m = (seconds % 3600) / 60;
            int s = seconds % 60;
            return $"{h}:{m:D2}:{s:D2}";
        }

        /// <summary>
        /// Computes the curvature at each point of a polyline defined by (x, y).
        /// </summary>
        /// <returns>Array of curvature values (same length as input).</returns>
        public static double[] ComputeCurvature(double[] xCoords, double[] yCoords)
        {
            int n = xCoords.Length;
            if (n != yCoords.Length || n < 2)
                throw new ArgumentException("x and y must have the same length and at least 2 points.");

            // Arc-length parameterization
            double[] ds = new double[n - 1];
            for (int i = 0; i < n - 1; i++)
            {
                double dx = xCoords[i + 1] - xCoords[i];
                double dy = yCoords[i + 1] - yCoords[i];
                ds[i] = Math.Sqrt(dx * dx + dy * dy);
            }

            double[] s = new double[n];
            s[0] = 0;
            for (int i = 0; i < n - 1; i++)
                s[i + 1] = s[i] + ds[i];

            // Numerical gradient using central differences
            double[] dx_ = NumericalGradient(xCoords, s);
            double[] dy_ = NumericalGradient(yCoords, s);
            double[] ddx = NumericalGradient(dx_, s);
            double[] ddy = NumericalGradient(dy_, s);

            double[] kappa = new double[n];
            for (int i = 0; i < n; i++)
            {
                double denom = Math.Pow(dx_[i] * dx_[i] + dy_[i] * dy_[i], 1.5);
                kappa[i] = denom > 1e-12 ? (dx_[i] * ddy[i] - dy_[i] * ddx[i]) / denom : 0.0;
            }

            return kappa;
        }

        private static double[] NumericalGradient(double[] f, double[] x)
        {
            int n = f.Length;
            double[] grad = new double[n];

            // Forward difference at start
            grad[0] = (n > 1) ? (f[1] - f[0]) / Math.Max(x[1] - x[0], 1e-12) : 0;

            // Central differences in the middle
            for (int i = 1; i < n - 1; i++)
            {
                double dx = Math.Max(x[i + 1] - x[i - 1], 1e-12);
                grad[i] = (f[i + 1] - f[i - 1]) / dx;
            }

            // Backward difference at end
            if (n > 1)
                grad[n - 1] = (f[n - 1] - f[n - 2]) / Math.Max(x[n - 1] - x[n - 2], 1e-12);

            return grad;
        }

        /// <summary>
        /// Linearly interpolates a value from a sorted table.
        /// Equivalent to numpy.interp (clamps to boundary values).
        /// </summary>
        public static double Interp(double x, double[] xp, double[] yp)
        {
            if (xp.Length != yp.Length)
                throw new ArgumentException("xp and yp must have the same length.");

            int n = xp.Length;
            if (x <= xp[0]) return yp[0];
            if (x >= xp[n - 1]) return yp[n - 1];

            int idx = Array.BinarySearch(xp, x);
            if (idx >= 0) return yp[idx];
            idx = ~idx;
            double t = (x - xp[idx - 1]) / (xp[idx] - xp[idx - 1]);
            return yp[idx - 1] + t * (yp[idx] - yp[idx - 1]);
        }
    }
}
