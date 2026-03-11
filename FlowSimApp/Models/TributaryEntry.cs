using System;

namespace FlowSim.Models
{
    /// <summary>
    /// 描述一条在干流特定桩号处汇入的支流。
    /// <para>
    /// 每个实例包含：支流的初算求解器、在干流上的汇流桩号、以及可选的精算工厂。
    /// 精算工厂以汇流点水位作为支流的下游水位边界，用于改善汇流点水位的物理一致性。
    /// </para>
    /// </summary>
    public sealed class TributaryEntry
    {
        /// <summary>
        /// 本支流的求解器。
        /// 精算完成后，此属性将被替换为精算版本的求解器。
        /// </summary>
        public Solver Solver { get; internal set; }

        /// <summary>本支流在干流上的汇流桩号（m）。</summary>
        public double JunctionChainageM { get; }

        /// <summary>
        /// （可选）精算工厂：接受"汇流点水位"下游边界条件，返回本支流的精算求解器。
        /// 为 <see langword="null"/> 时，本支流仅执行初算，不参与精算迭代。
        /// </summary>
        public Func<Boundary, Solver>? RefinedFactory { get; }

        /// <summary>
        /// 构造一个支流汇入描述对象。
        /// </summary>
        /// <param name="solver">支流初算求解器（已配置正常水深下游边界）。</param>
        /// <param name="junctionChainageM">支流在干流上的汇流桩号（m）。</param>
        /// <param name="refinedFactory">
        ///   （可选）精算工厂：接受汇流点水位边界，返回精算版支流求解器。
        /// </param>
        public TributaryEntry(
            Solver solver,
            double junctionChainageM,
            Func<Boundary, Solver>? refinedFactory = null)
        {
            Solver             = solver ?? throw new ArgumentNullException(nameof(solver));
            JunctionChainageM  = junctionChainageM;
            RefinedFactory     = refinedFactory;
        }
    }
}
