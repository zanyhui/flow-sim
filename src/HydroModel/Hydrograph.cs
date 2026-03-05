using System;
using MathNet.Numerics.Interpolation;

namespace HydroModel
{
    /// <summary>
    /// Represents a hydrograph (time series of flow or stage).
    /// Equivalent to hydrograph.py.
    /// </summary>
    public class Hydrograph
    {
        private double[,]? _table;
        private Func<double, double>? _customFunction;

        /// <summary>
        /// Creates a hydrograph with an optional custom function or table.
        /// </summary>
        /// <param name="function">Custom f(t) function. If null, linear interpolation is used.</param>
        /// <param name="table">
        /// 2D array with time (seconds) in column 0 and values in column 1.
        /// </param>
        public Hydrograph(Func<double, double>? function = null, double[,]? table = null)
        {
            _table = table;
            _customFunction = function;
        }

        /// <summary>
        /// Gets the hydrograph value at the specified time using linear interpolation.
        /// </summary>
        private double InterpolateHydrograph(double time)
        {
            if (_table == null)
                throw new InvalidOperationException("Hydrograph is not defined.");

            int n = _table.GetLength(0);
            double[] times = new double[n];
            double[] values = new double[n];
            for (int i = 0; i < n; i++)
            {
                times[i] = _table[i, 0];
                values[i] = _table[i, 1];
            }

            // Clamp to table bounds (same as numpy.interp behavior)
            if (time <= times[0]) return values[0];
            if (time >= times[n - 1]) return values[n - 1];

            // Linear interpolation
            int idx = Array.BinarySearch(times, time);
            if (idx >= 0) return values[idx];
            idx = ~idx; // first index greater than time
            double t0 = times[idx - 1], t1 = times[idx];
            double v0 = values[idx - 1], v1 = values[idx];
            return v0 + (v1 - v0) * (time - t0) / (t1 - t0);
        }

        /// <summary>
        /// Gets the hydrograph value at the specified time.
        /// </summary>
        public double GetAt(double time)
        {
            if (_customFunction != null)
                return _customFunction(time);
            return InterpolateHydrograph(time);
        }

        /// <summary>
        /// Sets the hydrograph data table.
        /// Time (seconds) in column 0, values in column 1.
        /// </summary>
        public void SetTable(double[,] table)
        {
            _table = table;
        }

        /// <summary>
        /// Sets a custom function for the hydrograph.
        /// </summary>
        public void SetFunction(Func<double, double> func)
        {
            _customFunction = func;
        }
    }
}
