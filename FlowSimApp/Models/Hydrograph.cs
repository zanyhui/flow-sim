using System;

namespace FlowSim.Models
{
    /// <summary>Time series of flow or stage values (hydrograph).</summary>
    public class Hydrograph
    {
        private double[,]? _table; // [n,2]: col0=time(s), col1=value
        private Func<double, double>? _function;

        public Hydrograph(Func<double, double>? function = null, double[,]? table = null)
        {
            _table = table;
            _function = function;
        }

        private double InterpolateHydrograph(double time)
        {
            if (_table == null)
                throw new InvalidOperationException("Hydrograph table is not defined.");
            int n = _table.GetLength(0);
            double[] times = new double[n];
            double[] values = new double[n];
            for (int i = 0; i < n; i++) { times[i] = _table[i, 0]; values[i] = _table[i, 1]; }
            return Hydraulics.Interp(time, times, values);
        }

        public void SetTable(double[,] table) => _table = table;
        public void SetFunction(Func<double, double> func) => _function = func;

        public double GetAt(double time) => _function != null ? _function(time) : InterpolateHydrograph(time);
    }
}
