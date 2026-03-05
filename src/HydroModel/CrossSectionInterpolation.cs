using System;

namespace HydroModel
{
    /// <summary>
    /// Interpolation helpers for CrossSection objects.
    /// Equivalent to the interpolate_cross_section function in cross_section.py.
    /// </summary>
    public static class CrossSectionInterpolation
    {
        private static double? InterpSafe(double? v1, double? v2, double w1, double w2)
        {
            if (v1 == null || v2 == null) return null;
            return v1.Value * w1 + v2.Value * w2;
        }

        /// <summary>
        /// Interpolates a new CrossSection from two neighboring sections.
        /// </summary>
        /// <param name="xs1">Upstream/left cross-section.</param>
        /// <param name="xs2">Downstream/right cross-section.</param>
        /// <param name="dist1">Distance from target location to xs1.</param>
        /// <param name="dist2">Distance from target location to xs2.</param>
        public static CrossSection Interpolate(CrossSection xs1, CrossSection xs2,
            double dist1, double dist2)
        {
            double totalDist = dist1 + dist2;

            if (totalDist < 1e-9) return xs1;
            if (dist1 < 1e-9) return xs1;
            if (dist2 < 1e-9) return xs2;

            // Closer section gets more weight
            double w1 = dist2 / totalDist;
            double w2 = dist1 / totalDist;

            double nL = xs1.NLeft * w1 + xs2.NLeft * w2;
            double nM = xs1.NMain * w1 + xs2.NMain * w2;
            double nR = xs1.NRight * w1 + xs2.NRight * w2;
            double? bedSlope = InterpSafe(xs1.BedSlope, xs2.BedSlope, w1, w2);
            double curvature = xs1.Curvature * w1 + xs2.Curvature * w2;

            // Both Trapezoidal
            if (xs1 is TrapezoidalSection ts1 && xs2 is TrapezoidalSection ts2)
            {
                double zBed = ts1.ZBed * w1 + ts2.ZBed * w2;
                double bMain = ts1.BMain * w1 + ts2.BMain * w2;
                double mMain = ts1.MMain * w1 + ts2.MMain * w2;

                double yBank1 = ts1.ZBank.HasValue ? ts1.ZBank.Value - ts1.ZBed : 0.0;
                double yBank2 = ts2.ZBank.HasValue ? ts2.ZBank.Value - ts2.ZBed : 0.0;
                double yBankNew = yBank1 * w1 + yBank2 * w2;

                double? zBankNew = yBankNew > 1e-6 ? (double?)(zBed + yBankNew) : null;

                double bFpL = ts1.BFpLeft * w1 + ts2.BFpLeft * w2;
                double bFpR = ts1.BFpRight * w1 + ts2.BFpRight * w2;
                double mFp = ts1.MFp * w1 + ts2.MFp * w2;

                return new TrapezoidalSection(
                    zBed, bMain, mMain, nM,
                    zBankNew, bFpL, bFpR, mFp,
                    nLeft: nL, nRight: nR,
                    bedSlope: bedSlope, curvature: curvature);
            }

            // At least one is Irregular — merge point clouds
            double[]? x1 = (xs1 is IrregularSection ir1) ? ir1.X : null;
            double[]? x2 = (xs2 is IrregularSection ir2) ? ir2.X : null;

            double[] xMaster;
            if (x1 != null && x2 != null)
            {
                // Union of x coordinates
                var union = new System.Collections.Generic.SortedSet<double>(x1);
                foreach (double v in x2) union.Add(v);
                xMaster = new double[union.Count];
                union.CopyTo(xMaster);
            }
            else if (x1 != null)
                xMaster = x1;
            else if (x2 != null)
                xMaster = x2;
            else
                throw new InvalidOperationException("Cannot interpolate: no x-coordinates found.");

            double[] zNew = new double[xMaster.Length];
            for (int i = 0; i < xMaster.Length; i++)
                zNew[i] = xs1.ZAt(xMaster[i]) * w1 + xs2.ZAt(xMaster[i]) * w2;

            var newCs = new IrregularSection(xMaster, zNew, nM, bedSlope, curvature);

            double lLim = xs1.LeftFloodplainLimit * w1 + xs2.LeftFloodplainLimit * w2;
            double rLim = xs1.RightFloodplainLimit * w1 + xs2.RightFloodplainLimit * w2;
            newCs.SetRoughnessParameters(nL, nM, nR, lLim, rLim);

            return newCs;
        }
    }
}
