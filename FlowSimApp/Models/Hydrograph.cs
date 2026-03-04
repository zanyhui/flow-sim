using System;

namespace FlowSim.Models
{
    /// <summary>
    /// 表示流量过程线或水位过程线（随时间变化的时间序列）。
    /// 支持两种数据来源：
    ///   1. 函数委托（Func&lt;double,double&gt;）：直接由解析函数给出任意时刻的值；
    ///   2. 二维数组表（列 0 = 时间/s，列 1 = 流量或水位）：通过线性插值获取中间值。
    /// </summary>
    public class Hydrograph
    {
        // 以二维数组存储时间序列：第 i 行 [time_s, value]
        private double[,]? _table;

        // 以解析函数存储：接受时间（秒）返回对应值
        private Func<double, double>? _function;

        /// <summary>
        /// 构造函数：可选传入解析函数或数组表，二者至少提供一种。
        /// </summary>
        /// <param name="function">可选，接受时间（秒）返回流量/水位的委托函数。</param>
        /// <param name="table">可选，二维数组，[n,2]，第 0 列为时间（秒），第 1 列为对应值。</param>
        public Hydrograph(Func<double, double>? function = null, double[,]? table = null)
        {
            _table = table;
            _function = function;
        }

        /// <summary>
        /// 在给定时间点对表格数据进行线性插值，返回对应值。
        /// 若时间超出范围，则外推到端点值（即夹紧）。
        /// </summary>
        /// <param name="time">查询时间（秒）。</param>
        /// <returns>插值得到的流量或水位值。</returns>
        private double InterpolateHydrograph(double time)
        {
            if (_table == null)
                throw new InvalidOperationException("Hydrograph table is not defined.");

            int n = _table.GetLength(0);          // 数据点个数
            double[] times = new double[n];        // 时间序列
            double[] values = new double[n];       // 对应值序列

            // 将二维数组拆分为两个一维数组，供 Hydraulics.Interp 使用
            for (int i = 0; i < n; i++) { times[i] = _table[i, 0]; values[i] = _table[i, 1]; }

            // 调用通用线性插值工具
            return Hydraulics.Interp(time, times, values);
        }

        /// <summary>替换内部数据表，不影响已设置的函数。</summary>
        /// <param name="table">新的 [n,2] 时间序列数组。</param>
        public void SetTable(double[,] table) => _table = table;

        /// <summary>替换内部解析函数，不影响已设置的数据表。</summary>
        /// <param name="func">新的解析函数。</param>
        public void SetFunction(Func<double, double> func) => _function = func;

        /// <summary>
        /// 获取给定时刻的流量或水位值。
        /// 若已设置解析函数则优先使用函数；否则使用表格插值。
        /// </summary>
        /// <param name="time">查询时间（秒）。</param>
        /// <returns>对应时刻的流量（m³/s）或水位（m）。</returns>
        public double GetAt(double time) => _function != null ? _function(time) : InterpolateHydrograph(time);
    }
}
