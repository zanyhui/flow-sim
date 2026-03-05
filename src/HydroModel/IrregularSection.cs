using System;
using System.Collections.Generic;
using System.Linq;

namespace HydroModel
{
    /// <summary>
    /// Cross-section defined by a polyline (x, z coordinates).
    /// Handles complex geometries including multiple disconnected sub-channels.
    /// Equivalent to IrregularSection in cross_section.py.
    /// </summary>
    public class IrregularSection : CrossSection
    {
        public double[] X { get; private set; }
        public double[] Z { get; private set; }

        private readonly double _zMin;
        private readonly double _width;

        public IrregularSection(double[] x, double[] z,
            double? n = null, double? bedSlope = null, double curvature = 0.0)
            : base(n, bedSlope, curvature)
        {
            if (x.Length != z.Length)
                throw new ArgumentException("x and z must have the same length.");
            if (x.Length < 2)
                throw new ArgumentException("x and z must have at least 2 points.");

            // Sort by x
            int[] idx = Enumerable.Range(0, x.Length).OrderBy(i => x[i]).ToArray();
            X = idx.Select(i => x[i]).ToArray();
            Z = idx.Select(i => z[i]).ToArray();

            _zMin = Z.Min();
            _width = X.Max() - X.Min();

            LeftFloodplainLimit = X[0];
            RightFloodplainLimit = X[X.Length - 1];
        }

        public override double ZMin => _zMin;
        public override double Width => _width;

        public override (double A, double P, double R, double T) Properties(double hw)
        {
            if (_lastHw.HasValue && hw == _lastHw.Value)
                return _lastRes!.Value;

            if (hw <= _zMin)
            {
                _lastHw = hw;
                _lastRes = (0, 0, 0, 0);
                return (0, 0, 0, 0);
            }

            double[] hNodes = new double[Z.Length];
            bool anyWet = false;
            for (int i = 0; i < Z.Length; i++)
            {
                hNodes[i] = hw - Z[i];
                if (hNodes[i] > 0) anyWet = true;
            }

            if (!anyWet)
            {
                _lastHw = hw;
                _lastRes = (0, 0, 0, 0);
                return (0, 0, 0, 0);
            }

            // Find continuous wetted segments
            var wetSegments = new List<(int start, int end)>();
            int n = Z.Length;
            int idx = 0;
            while (idx < n)
            {
                if (hNodes[idx] > 0)
                {
                    int start = idx;
                    while (idx + 1 < n && hNodes[idx + 1] > 0)
                        idx++;
                    wetSegments.Add((start, idx));
                }
                idx++;
            }

            double aTotal = 0, pTotal = 0, tTotal = 0;

            foreach (var (i0, iN) in wetSegments)
            {
                var xSeg = new List<double>(X[i0..(iN + 1)]);
                var zSeg = new List<double>(Z[i0..(iN + 1)]);

                // Add left intersection
                if (i0 > 0 && Z[i0 - 1] > hw)
                {
                    double z0 = Z[i0 - 1], z1 = Z[i0];
                    double x0 = X[i0 - 1], x1 = X[i0];
                    double t = (hw - z0) / (z1 - z0);
                    xSeg.Insert(0, x0 + t * (x1 - x0));
                    zSeg.Insert(0, hw);
                }

                // Add right intersection
                if (iN < n - 1 && Z[iN + 1] > hw)
                {
                    double z0 = Z[iN], z1 = Z[iN + 1];
                    double x0 = X[iN], x1 = X[iN + 1];
                    double t = (hw - z0) / (z1 - z0);
                    xSeg.Add(x0 + t * (x1 - x0));
                    zSeg.Add(hw);
                }

                int m = xSeg.Count;
                double A = 0, P = 0;
                for (int i = 0; i < m - 1; i++)
                {
                    double dx = xSeg[i + 1] - xSeg[i];
                    double d0 = Math.Max(hw - zSeg[i], 0);
                    double d1 = Math.Max(hw - zSeg[i + 1], 0);
                    A += 0.5 * (d0 + d1) * dx;

                    double dz = zSeg[i + 1] - zSeg[i];
                    P += Math.Sqrt(dx * dx + dz * dz);
                }
                double T = xSeg[m - 1] - xSeg[0];

                aTotal += A;
                pTotal += P;
                tTotal += T;
            }

            double rTotal = pTotal > 0 ? aTotal / pTotal : 0;
            var res = (aTotal, pTotal, rTotal, tTotal);
            _lastHw = hw;
            _lastRes = res;
            return res;
        }

        /// <summary>Returns the wetted sub-channels at a given water level.</summary>
        public List<(double[] x, double[] z)> GetSubChannels(double hw)
        {
            var result = new List<(double[] x, double[] z)>();
            int n = Z.Length;
            int i = 0;

            while (i < n)
            {
                if (Z[i] >= hw) { i++; continue; }

                int start = i;
                while (i < n && Z[i] < hw) i++;
                int end = i;

                if (end - start < 2) continue;

                var xSeg = new List<double>(X[start..end]);
                var zSeg = new List<double>(Z[start..end]);

                if (start > 0 && Z[start - 1] >= hw)
                {
                    double x0 = Utility.Interp(hw, new[] { Z[start - 1], Z[start] },
                        new[] { X[start - 1], X[start] });
                    xSeg.Insert(0, x0);
                    zSeg.Insert(0, hw);
                }
                if (end < n && Z[end - 1] < hw && Z[end] >= hw)
                {
                    double x1 = Utility.Interp(hw, new[] { Z[end - 1], Z[end] },
                        new[] { X[end - 1], X[end] });
                    xSeg.Add(x1);
                    zSeg.Add(hw);
                }

                result.Add((xSeg.ToArray(), zSeg.ToArray()));
            }

            return result;
        }

        public override double FrictionSlope(double depth, double flow)
        {
            double hw = depth + ZMin;
            var subChs = GetSubChannels(hw);

            if (subChs.Count <= 1)
                return base.FrictionSlope(depth, flow);

            double kSum = 0;
            foreach (var (xs, zs) in subChs)
            {
                var sub = new IrregularSection(xs, zs);
                sub.SetRoughnessParameters(NLeft, NMain, NRight,
                    LeftFloodplainLimit, RightFloodplainLimit);
                double kj = sub.Conveyance(hw);
                kSum += Math.Pow(kj, 1.5);
            }

            double kTotal = Math.Pow(kSum, 2.0 / 3.0);
            return Hydraulics.FrictionSlope(flow, k: kTotal);
        }

        public override double DFrictionSlopeDArea(double depth, double flow)
        {
            double hw = depth + ZMin;
            var subChs = GetSubChannels(hw);

            if (subChs.Count <= 1)
                return base.DFrictionSlopeDArea(depth, flow);

            double kSum = 0, dkSum = 0;
            foreach (var (xs, zs) in subChs)
            {
                var sub = new IrregularSection(xs, zs);
                sub.SetRoughnessParameters(NLeft, NMain, NRight,
                    LeftFloodplainLimit, RightFloodplainLimit);
                double kj = sub.Conveyance(hw);
                double dkj = sub.DConveyanceDArea(hw);
                kSum += Math.Pow(kj, 1.5);
                dkSum += 1.5 * Math.Sqrt(kj) * dkj;
            }

            double kEq = Math.Pow(kSum, 2.0 / 3.0);
            double dkEq = (2.0 / 3.0) * Math.Pow(kSum, -1.0 / 3.0) * dkSum;
            return Hydraulics.DFrictionSlopeDArea(flow, k: kEq, dKdA: dkEq);
        }

        public override double DFrictionSlopeDFlow(double depth, double flow)
        {
            double hw = depth + ZMin;
            var subChs = GetSubChannels(hw);

            if (subChs.Count <= 1)
                return base.DFrictionSlopeDFlow(depth, flow);

            double kSum = 0;
            foreach (var (xs, zs) in subChs)
            {
                var sub = new IrregularSection(xs, zs);
                sub.SetRoughnessParameters(NLeft, NMain, NRight,
                    LeftFloodplainLimit, RightFloodplainLimit);
                kSum += Math.Pow(sub.Conveyance(hw), 1.5);
            }

            double kEq = Math.Pow(kSum, 2.0 / 3.0);
            return Hydraulics.DFrictionSlopeDFlow(flow, k: kEq);
        }

        public override double GetEquivalentN(double hw)
        {
            if (_lastHwN.HasValue && hw == _lastHwN.Value)
                return _lastN!.Value;

            double SubsectionConveyance(double xMin, double xMax, double nValue)
            {
                var mask = X.Zip(Z, (xi, zi) => (xi, zi))
                    .Where(p => p.xi >= xMin && p.xi <= xMax)
                    .ToList();
                if (mask.Count < 2) return 0;

                var subXs = mask.Select(p => p.xi).ToArray();
                var subZs = mask.Select(p => p.zi).ToArray();
                var subSection = new IrregularSection(subXs, subZs);

                double A = subSection.Area(hw);
                if (A <= 0) return 0;
                double P = subSection.WettedPerimeter(hw);
                if (P <= 0) return 0;
                double R = A / P;
                return Hydraulics.Conveyance(A, nValue, R);
            }

            double kLeft = SubsectionConveyance(X[0], LeftFloodplainLimit, NLeft);
            double kMain = SubsectionConveyance(LeftFloodplainLimit, RightFloodplainLimit, NMain);
            double kRight = SubsectionConveyance(RightFloodplainLimit, X[X.Length - 1], NRight);

            double aTotal = Area(hw);
            double pTotal = WettedPerimeter(hw);

            if (aTotal <= 0 || pTotal <= 0)
                return NMain;

            double rTotal = aTotal / pTotal;
            double kTotal = Math.Pow(
                Math.Pow(kLeft, 1.5) + Math.Pow(kMain, 1.5) + Math.Pow(kRight, 1.5),
                2.0 / 3.0);

            if (kTotal <= 0) return NMain;

            double nEq = aTotal * Math.Pow(rTotal, 2.0 / 3.0) / kTotal;
            _lastHwN = hw;
            _lastN = nEq;
            return nEq;
        }

        public override double Conveyance(double hw)
        {
            double A = Area(hw);
            if (A <= 0) return 0;
            double n = GetEquivalentN(hw);
            double R = HydraulicRadius(hw);
            return Hydraulics.Conveyance(A, n, R);
        }

        public override double DConveyanceDArea(double hw)
        {
            double A = Area(hw);
            if (A <= 0) return 0;
            double n = GetEquivalentN(hw);
            double R = HydraulicRadius(hw);
            double dRdA = DHydraulicRadiusDArea(hw);
            return Hydraulics.DConveyanceDArea(A, n, R, dRdA);
        }

        public override double DHydraulicRadiusDArea(double hw)
        {
            const double dh = 1e-6;
            double a1 = Area(hw - dh);
            double a2 = Area(hw + dh);
            if (a2 - a1 == 0) return 0;
            double r1 = HydraulicRadius(hw - dh);
            double r2 = HydraulicRadius(hw + dh);
            return (r2 - r1) / (a2 - a1);
        }

        public override double DAreaDDepth(double hw)
        {
            const double dh = 1e-6;
            double a1 = Area(hw - dh);
            double a2 = Area(hw + dh);
            return (a2 - a1) / (2.0 * dh);
        }

        public override double ZAt(double x)
        {
            return Utility.Interp(x, X, Z);
        }
    }
}
