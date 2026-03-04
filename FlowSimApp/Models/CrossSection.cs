using System;
using System.Linq;

namespace FlowSim.Models
{
    /// <summary>Abstract base class for hydraulic cross-sections.</summary>
    public abstract class CrossSection
    {
        public double NLeft { get; set; }
        public double NMain { get; set; }
        public double NRight { get; set; }
        public double LeftFpLimit { get; set; } = 0.0;
        public double RightFpLimit { get; set; } = 0.0;
        public double Curvature { get; set; } = 0.0;
        public double? BedSlope { get; set; }

        protected double? _lastHw;
        protected (double A, double P, double R, double T)? _lastRes;

        protected CrossSection(double n = 0.03, double? bedSlope = null, double curvature = 0.0)
        {
            NLeft = NMain = NRight = n;
            BedSlope = bedSlope;
            Curvature = curvature;
        }

        public abstract double ZMin { get; }
        public abstract double Width { get; }
        public abstract (double A, double P, double R, double T) Properties(double hw);
        public abstract double GetEquivalentN(double hw);
        public abstract double Conveyance(double hw);
        public abstract double DConveyance_DA(double hw);
        public abstract double DRadius_DA(double hw);
        public abstract double DArea_Dh(double hw);
        public abstract double ZAt(double x);

        public double Area(double hw) => Properties(hw).A;
        public double WettedPerimeter(double hw) => Properties(hw).P;
        public double HydraulicRadius(double hw) => Properties(hw).R;
        public double TopWidth(double hw) => Properties(hw).T;

        public virtual double FrictionSlope(double h, double Q)
        {
            double hw = h + ZMin;
            double K = Conveyance(hw);
            return Hydraulics.FrictionSlope(Q, K);
        }

        public virtual double DFrictionSlope_DA(double h, double Q)
        {
            double hw = h + ZMin;
            double K = Conveyance(hw);
            double dK = DConveyance_DA(hw);
            return Hydraulics.DFrictionSlope_DA(Q, K, dK);
        }

        public virtual double DFrictionSlope_DQ(double h, double Q)
        {
            double hw = h + ZMin;
            double K = Conveyance(hw);
            return Hydraulics.DFrictionSlope_DQ(Q, K);
        }

        public double CurvatureSlope(double h, double Q)
        {
            if (Curvature == 0) return 0.0;
            double hw = h + ZMin;
            double n = GetEquivalentN(hw);
            var (A, P, R, T) = Properties(hw);
            return Hydraulics.CurvatureSlope(h, T, A, Q, n, R, 1.0 / Curvature);
        }

        public double DCurvatureSlope_DA(double h, double Q)
        {
            if (Math.Abs(Curvature) <= 1e-12) return 0.0;
            double hw = h + ZMin;
            double n = GetEquivalentN(hw);
            var (A, P, R, T) = Properties(hw);
            double dR_dA = DRadius_DA(hw);
            return Hydraulics.DCurvatureSlope_DA(h, A, Q, n, R, 1.0 / Curvature, dR_dA, T) * DArea_Dh(hw);
        }

        public double DCurvatureSlope_DQ(double h, double Q)
        {
            if (Math.Abs(Curvature) <= 1e-12) return 0.0;
            double hw = h + ZMin;
            double n = GetEquivalentN(hw);
            var (A, P, R, T) = Properties(hw);
            return Hydraulics.DCurvatureSlope_DQ(h, T, A, Q, n, R, 1.0 / Curvature);
        }

        public double NormalFlow(double hw)
        {
            if (BedSlope == null || BedSlope <= 0.0) return 0.0;
            double K = Conveyance(hw);
            return Hydraulics.NormalFlow(BedSlope.Value, K);
        }

        public double NormalDepth(double Qtarget, double hwMax = double.NaN)
        {
            double zMin = ZMin;
            if (double.IsNaN(hwMax)) hwMax = zMin + 100.0;
            double F(double hw) => Qtarget - NormalFlow(hw);
            try
            {
                double hw = Hydraulics.Brentq(F, zMin + 1e-6, hwMax);
                return hw - zMin;
            }
            catch
            {
                if (F(zMin + 1e-6) < 0) return 0.0;
                return hwMax - zMin;
            }
        }
    }

    /// <summary>Trapezoidal cross-section.</summary>
    public class TrapezoidalSection : CrossSection
    {
        private readonly double _bMain;
        private readonly double _mMain;
        private readonly double _zBed;

        public TrapezoidalSection(double bMain, double mMain, double zBed, double nMain,
                                   double? bedSlope = null, double curvature = 0.0)
            : base(nMain, bedSlope, curvature)
        {
            _bMain = bMain;
            _mMain = mMain;
            _zBed = zBed;
        }

        public override double ZMin => _zBed;
        public override double Width => _bMain;

        public override (double A, double P, double R, double T) Properties(double hw)
        {
            double h = hw - _zBed;
            if (h <= 0) return (0, 0, 0, 0);
            double T = _bMain + 2.0 * _mMain * h;
            double A = (_bMain + _mMain * h) * h;
            double P = _bMain + 2.0 * h * Math.Sqrt(1.0 + _mMain * _mMain);
            double R = P > 0 ? A / P : 0;
            return (A, P, R, T);
        }

        public override double GetEquivalentN(double hw) => NMain;

        public override double Conveyance(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (A <= 0) return 0;
            return Hydraulics.Conveyance(A, NMain, R);
        }

        public override double DConveyance_DA(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (A <= 0) return 0;
            double dR_dA = DRadius_DA(hw);
            return Hydraulics.DConveyance_DA(A, NMain, R, dR_dA);
        }

        public override double DRadius_DA(double hw)
        {
            double h = hw - _zBed;
            if (h <= 0) return 0;
            double T = _bMain + 2.0 * _mMain * h;
            double A = (_bMain + _mMain * h) * h;
            double P = _bMain + 2.0 * h * Math.Sqrt(1.0 + _mMain * _mMain);
            double dP_dh = 2.0 * Math.Sqrt(1.0 + _mMain * _mMain);
            double dA_dh = T;
            double dP_dA = dP_dh / dA_dh;
            return (P - A * dP_dA) / (P * P);
        }

        public override double DArea_Dh(double hw)
        {
            double h = hw - _zBed;
            if (h <= 0) return _bMain;
            return _bMain + 2.0 * _mMain * h;
        }

        public override double ZAt(double x) => _zBed;
    }

    /// <summary>Irregular cross-section defined by (x, z) polyline points.</summary>
    public class IrregularSection : CrossSection
    {
        public double[] X { get; private set; }
        public double[] Z { get; private set; }
        private readonly double _zMin;
        private readonly double _width;

        public IrregularSection(double[] x, double[] z, double n = 0.03,
                                 double? bedSlope = null, double curvature = 0.0)
            : base(n, bedSlope, curvature)
        {
            int[] idx = Enumerable.Range(0, x.Length).OrderBy(i => x[i]).ToArray();
            X = idx.Select(i => x[i]).ToArray();
            Z = idx.Select(i => z[i]).ToArray();
            _zMin = Z.Min();
            _width = X.Max() - X.Min();
            LeftFpLimit = X[0];
            RightFpLimit = X[X.Length - 1];
        }

        public override double ZMin => _zMin;
        public override double Width => _width;

        public override (double A, double P, double R, double T) Properties(double hw)
        {
            if (_lastHw.HasValue && _lastHw.Value == hw && _lastRes.HasValue)
                return _lastRes.Value;

            if (hw <= _zMin)
            {
                var r = (0.0, 0.0, 0.0, 0.0);
                _lastHw = hw; _lastRes = r;
                return r;
            }

            double[] hNodes = Z.Select(z => hw - z).ToArray();
            if (!hNodes.Any(h => h > 0))
            {
                var r = (0.0, 0.0, 0.0, 0.0);
                _lastHw = hw; _lastRes = r;
                return r;
            }

            var wetSegments = FindWetSegments(hNodes);

            double A_total = 0, P_total = 0, T_total = 0;

            foreach (var (i0, iN) in wetSegments)
            {
                var (xSeg, zSeg) = BuildSegment(i0, iN, hw);
                double[] dSeg = zSeg.Select(z => Math.Max(hw - z, 0.0)).ToArray();

                double A = 0;
                for (int k = 0; k < xSeg.Length - 1; k++)
                    A += 0.5 * (dSeg[k] + dSeg[k + 1]) * (xSeg[k + 1] - xSeg[k]);

                double P = 0;
                for (int k = 0; k < xSeg.Length - 1; k++)
                {
                    double dx = xSeg[k + 1] - xSeg[k];
                    double dz = zSeg[k + 1] - zSeg[k];
                    P += Math.Sqrt(dx * dx + dz * dz);
                }

                double T = xSeg[xSeg.Length - 1] - xSeg[0];

                A_total += A;
                P_total += P;
                T_total += T;
            }

            double R_total = P_total > 0 ? A_total / P_total : 0.0;
            var res = (A_total, P_total, R_total, T_total);
            _lastHw = hw; _lastRes = res;
            return res;
        }

        private System.Collections.Generic.List<(int start, int end)> FindWetSegments(double[] hNodes)
        {
            var segs = new System.Collections.Generic.List<(int, int)>();
            int i = 0, n = hNodes.Length;
            while (i < n)
            {
                if (hNodes[i] > 0)
                {
                    int start = i;
                    while (i + 1 < n && hNodes[i + 1] > 0) i++;
                    segs.Add((start, i));
                }
                i++;
            }
            return segs;
        }

        private (double[] xSeg, double[] zSeg) BuildSegment(int i0, int iN, double hw)
        {
            var xList = new System.Collections.Generic.List<double>();
            var zList = new System.Collections.Generic.List<double>();

            if (i0 > 0 && Z[i0 - 1] > hw)
            {
                double t = (hw - Z[i0 - 1]) / (Z[i0] - Z[i0 - 1]);
                double xl = X[i0 - 1] + t * (X[i0] - X[i0 - 1]);
                xList.Add(xl); zList.Add(hw);
            }

            for (int k = i0; k <= iN; k++) { xList.Add(X[k]); zList.Add(Z[k]); }

            if (iN < X.Length - 1 && Z[iN + 1] > hw)
            {
                double t = (hw - Z[iN]) / (Z[iN + 1] - Z[iN]);
                double xr = X[iN] + t * (X[iN + 1] - X[iN]);
                xList.Add(xr); zList.Add(hw);
            }

            return (xList.ToArray(), zList.ToArray());
        }

        public override double GetEquivalentN(double hw)
        {
            double A_total = Area(hw);
            double P_total = WettedPerimeter(hw);
            if (A_total <= 0 || P_total <= 0) return NMain;
            double R_total = A_total / P_total;

            double GetSubsectionK(double xMin, double xMax, double n)
            {
                var mask = X.Select((xi, i) => (xi, i)).Where(t => t.xi >= xMin && t.xi <= xMax).Select(t => t.i).ToArray();
                if (mask.Length < 2) return 0;
                var xs = CreateSubSection(X, Z, mask, n);
                double A = xs.Area(hw);
                if (A <= 0) return 0;
                double P = xs.WettedPerimeter(hw);
                if (P <= 0) return 0;
                double R = A / P;
                return Hydraulics.Conveyance(A, n, R);
            }

            double KLeft = GetSubsectionK(X[0], LeftFpLimit, NLeft);
            double KMain = GetSubsectionK(LeftFpLimit, RightFpLimit, NMain);
            double KRight = GetSubsectionK(RightFpLimit, X[X.Length - 1], NRight);

            // Composite conveyance using Lotter's (Horton-Einstein) method:
            // K_total = (K_left^1.5 + K_main^1.5 + K_right^1.5)^(2/3)
            double K_total = Math.Pow(Math.Pow(KLeft, 1.5) + Math.Pow(KMain, 1.5) + Math.Pow(KRight, 1.5), 2.0 / 3.0);
            if (K_total <= 0) return NMain;

            return A_total * Math.Pow(R_total, 2.0 / 3.0) / K_total;
        }

        private static IrregularSection CreateSubSection(double[] X, double[] Z, int[] indices, double n)
        {
            double[] xs = indices.Select(i => X[i]).ToArray();
            double[] zs = indices.Select(i => Z[i]).ToArray();
            return new IrregularSection(xs, zs, n);
        }

        public override double Conveyance(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (A <= 0) return 0;
            double n = GetEquivalentN(hw);
            return Hydraulics.Conveyance(A, n, R);
        }

        public override double DConveyance_DA(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (A <= 0) return 0;
            double n = GetEquivalentN(hw);
            double dR_dA = DRadius_DA(hw);
            return Hydraulics.DConveyance_DA(A, n, R, dR_dA);
        }

        public override double DRadius_DA(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (P <= 0 || T <= 0) return 0;
            double dh = 1e-4;
            double P2 = Properties(hw + dh).P;
            double dP_dh = (P2 - P) / dh;
            double dP_dA = dP_dh / T;
            return (P - A * dP_dA) / (P * P);
        }

        public override double DArea_Dh(double hw)
        {
            return Properties(hw).T;
        }

        public override double ZAt(double x)
        {
            return Hydraulics.Interp(x, X, Z);
        }
    }

    /// <summary>Helper to interpolate between two cross sections by distance weighting.</summary>
    public static class CrossSectionInterpolator
    {
        public static CrossSection Interpolate(CrossSection xs1, CrossSection xs2, double dist1, double dist2)
        {
            double totalDist = dist1 + dist2;
            if (totalDist <= 0) return xs1;

            double w1 = dist2 / totalDist;
            double w2 = dist1 / totalDist;

            if (xs1 is TrapezoidalSection t1 && xs2 is TrapezoidalSection t2)
            {
                double b = t1.Width * w1 + t2.Width * w2;
                double z = t1.ZMin * w1 + t2.ZMin * w2;
                double n = t1.NMain * w1 + t2.NMain * w2;
                double S0 = (t1.BedSlope.HasValue && t2.BedSlope.HasValue)
                    ? t1.BedSlope.Value * w1 + t2.BedSlope.Value * w2
                    : (t1.BedSlope ?? t2.BedSlope) ?? 0;
                return new TrapezoidalSection(b, 0, z, n, S0);
            }
            else if (xs1 is IrregularSection ir1 && xs2 is IrregularSection ir2)
            {
                double[] x = ir1.X;
                double[] z = new double[x.Length];
                for (int i = 0; i < x.Length; i++)
                {
                    double z2 = Hydraulics.Interp(x[i], ir2.X, ir2.Z);
                    z[i] = ir1.Z[i] * w1 + z2 * w2;
                }
                double n = ir1.NMain * w1 + ir2.NMain * w2;
                double S0 = (ir1.BedSlope.HasValue && ir2.BedSlope.HasValue)
                    ? ir1.BedSlope.Value * w1 + ir2.BedSlope.Value * w2
                    : (ir1.BedSlope ?? ir2.BedSlope) ?? 0;
                return new IrregularSection(x, z, n, S0);
            }
            else
            {
                return xs1;
            }
        }
    }
}
