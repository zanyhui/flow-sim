using System;

namespace HydroModel
{
    /// <summary>
    /// Cross-section with a trapezoidal geometry (simple, compound, or rectangular).
    /// Equivalent to TrapezoidalSection in cross_section.py.
    /// </summary>
    public class TrapezoidalSection : CrossSection
    {
        public double ZBed { get; }
        public double BMain { get; }
        public double MMain { get; }
        public double? ZBank { get; }
        public double BFpLeft { get; }
        public double BFpRight { get; }
        public double MFp { get; }

        private readonly bool _isCompound;
        private readonly bool _isRect;
        private readonly double _zMin;
        private readonly double _bankfullDepth;
        private readonly double _tMainAtBank;
        private readonly double _widthAtBank;

        public TrapezoidalSection(double zBed, double bMain, double mMain, double nMain,
            double? zBank = null, double bFpLeft = 0, double bFpRight = 0, double mFp = 0,
            double nLeft = 0.03, double nRight = 0.03,
            double? bedSlope = null, double curvature = 0.0)
            : base(nMain, bedSlope, curvature)
        {
            ZBed = zBed;
            BMain = bMain;
            MMain = mMain;
            _zMin = zBed;

            if (zBank.HasValue)
            {
                if (zBank.Value <= zBed)
                    throw new ArgumentException("Bank elevation z_bank must be above bed z_bed.");

                _isCompound = true;
                ZBank = zBank;
                BFpLeft = bFpLeft;
                BFpRight = bFpRight;
                MFp = mFp;

                _bankfullDepth = zBank.Value - zBed;
                _tMainAtBank = bMain + 2.0 * mMain * _bankfullDepth;

                LeftFloodplainLimit = -_tMainAtBank / 2.0;
                RightFloodplainLimit = _tMainAtBank / 2.0;

                _widthAtBank = bFpLeft + _tMainAtBank + bFpRight;
            }
            else
            {
                _isCompound = false;
                ZBank = null;
                BFpLeft = 0;
                BFpRight = 0;
                MFp = 0;
                LeftFloodplainLimit = double.NegativeInfinity;
                RightFloodplainLimit = double.PositiveInfinity;
            }

            _isRect = !_isCompound && mMain == 0.0;

            SetRoughnessParameters(nLeft, nMain, nRight, LeftFloodplainLimit, RightFloodplainLimit);
        }

        public override double ZMin => _zMin;
        public override double Width => double.PositiveInfinity;

        public override (double A, double P, double R, double T) Properties(double hw)
        {
            if (_lastHw.HasValue && hw == _lastHw.Value)
                return _lastRes!.Value;

            double depth = Math.Max(0.0, hw - ZBed);
            if (depth <= 0.0)
            {
                _lastHw = hw;
                _lastRes = (0, 0, 0, 0);
                return (0, 0, 0, 0);
            }

            double A, P, T;

            if (_isRect)
            {
                A = BMain * depth;
                P = BMain + 2.0 * depth;
                T = BMain;
            }
            else if (!_isCompound)
            {
                T = BMain + 2.0 * MMain * depth;
                A = (BMain + T) / 2.0 * depth;
                P = BMain + 2.0 * depth * Math.Sqrt(1.0 + MMain * MMain);
            }
            else
            {
                if (depth <= _bankfullDepth)
                {
                    T = BMain + 2.0 * MMain * depth;
                    A = (BMain + T) / 2.0 * depth;
                    P = BMain + 2.0 * depth * Math.Sqrt(1.0 + MMain * MMain);
                }
                else
                {
                    double depthFp = depth - _bankfullDepth;

                    double aMain = (BMain + _tMainAtBank) / 2.0 * _bankfullDepth;
                    double pMain = BMain + 2.0 * _bankfullDepth * Math.Sqrt(1.0 + MMain * MMain);

                    double aLeft = (BFpLeft + 0.5 * MFp * depthFp) * depthFp;
                    double pLeft = BFpLeft + depthFp * Math.Sqrt(1.0 + MFp * MFp);

                    double aRight = (BFpRight + 0.5 * MFp * depthFp) * depthFp;
                    double pRight = BFpRight + depthFp * Math.Sqrt(1.0 + MFp * MFp);

                    A = aMain + aLeft + aRight;
                    P = pMain + pLeft + pRight;
                    T = _widthAtBank + 2.0 * MFp * depthFp;
                }
            }

            double R = P > 0 ? A / P : 0;
            var res = (A, P, R, T);
            _lastHw = hw;
            _lastRes = res;
            return res;
        }

        private ((double A, double P, double R) left, (double A, double P, double R) main,
            (double A, double P, double R) right) GetSubsectionProps(double hw)
        {
            double depth = Math.Max(0.0, hw - ZBed);
            if (depth <= 0.0)
                return ((0, 0, 0), (0, 0, 0), (0, 0, 0));

            if (!_isCompound || depth <= _bankfullDepth)
            {
                var (A, P, R, _) = Properties(hw);
                return ((0, 0, 0), (A, P, R), (0, 0, 0));
            }

            double depthFp = depth - _bankfullDepth;

            double aMain = (BMain + _tMainAtBank) / 2.0 * _bankfullDepth + _tMainAtBank * depthFp;
            double pMainBed = BMain + 2.0 * _bankfullDepth * Math.Sqrt(1.0 + MMain * MMain);
            double rMain = pMainBed > 0 ? aMain / pMainBed : 0;

            double aLeft = (BFpLeft + 0.5 * MFp * depthFp) * depthFp;
            double pLeftBed = BFpLeft + depthFp * Math.Sqrt(1.0 + MFp * MFp);
            double rLeft = pLeftBed > 0 ? aLeft / pLeftBed : 0;

            double aRight = (BFpRight + 0.5 * MFp * depthFp) * depthFp;
            double pRightBed = BFpRight + depthFp * Math.Sqrt(1.0 + MFp * MFp);
            double rRight = pRightBed > 0 ? aRight / pRightBed : 0;

            return ((aLeft, pLeftBed, rLeft), (aMain, pMainBed, rMain), (aRight, pRightBed, rRight));
        }

        public override double GetEquivalentN(double hw)
        {
            if (_lastHwN.HasValue && hw == _lastHwN.Value)
                return _lastN!.Value;

            if (!_isCompound)
            {
                _lastHwN = hw;
                _lastN = NMain;
                return NMain;
            }

            var (left, main, right) = GetSubsectionProps(hw);
            double kLeft = Hydraulics.Conveyance(left.A, NLeft, left.R);
            double kMain = Hydraulics.Conveyance(main.A, NMain, main.R);
            double kRight = Hydraulics.Conveyance(right.A, NRight, right.R);

            var (aTotal, _, rTotal, _) = Properties(hw);
            if (aTotal <= 0 || rTotal <= 0) return NMain;

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
            if (!_isCompound)
            {
                var (A, _, R, _) = Properties(hw);
                return Hydraulics.Conveyance(A, NMain, R);
            }

            var (left, main, right) = GetSubsectionProps(hw);
            double kLeft = Hydraulics.Conveyance(left.A, NLeft, left.R);
            double kMain = Hydraulics.Conveyance(main.A, NMain, main.R);
            double kRight = Hydraulics.Conveyance(right.A, NRight, right.R);

            return Math.Pow(
                Math.Pow(kLeft, 1.5) + Math.Pow(kMain, 1.5) + Math.Pow(kRight, 1.5),
                2.0 / 3.0);
        }

        public override double DConveyanceDArea(double hw)
        {
            var (A, _, R, _) = Properties(hw);
            if (A <= 0) return 0;
            double n = GetEquivalentN(hw);
            double dRdA = DHydraulicRadiusDArea(hw);
            return Hydraulics.DConveyanceDArea(A, n, R, dRdA);
        }

        public override double DHydraulicRadiusDArea(double hw)
        {
            var (A, P, R, T) = Properties(hw);
            if (P <= 0 || T <= 0) return 0;

            double depth = Math.Max(0.0, hw - ZBed);
            double dPdH;

            if (_isRect)
                dPdH = 2.0;
            else if (!_isCompound)
                dPdH = 2.0 * Math.Sqrt(1.0 + MMain * MMain);
            else
            {
                dPdH = depth <= _bankfullDepth
                    ? 2.0 * Math.Sqrt(1.0 + MMain * MMain)
                    : 2.0 * Math.Sqrt(1.0 + MFp * MFp);
            }

            double dHdA = 1.0 / T;
            double dPdA = dPdH * dHdA;
            return (P - A * dPdA) / (P * P);
        }

        public override double DAreaDDepth(double hw) => TopWidth(hw);

        public override double ZAt(double x)
        {
            if (_isRect)
                return (x > -BMain / 2.0 && x < BMain / 2.0) ? ZBed : double.PositiveInfinity;

            if (!_isCompound)
            {
                if (x >= -BMain / 2.0 && x <= BMain / 2.0) return ZBed;
                double dist = x > BMain / 2.0 ? x - BMain / 2.0 : -x - BMain / 2.0;
                return ZBed + dist / MMain;
            }

            // Compound
            if (x >= LeftFloodplainLimit && x <= RightFloodplainLimit)
            {
                if (x >= -BMain / 2.0 && x <= BMain / 2.0) return ZBed;
                double dist = x > BMain / 2.0 ? x - BMain / 2.0 : -x - BMain / 2.0;
                return ZBed + dist / MMain;
            }
            else if (x < LeftFloodplainLimit)
            {
                double xLeftBedOuter = LeftFloodplainLimit - BFpLeft;
                if (x >= xLeftBedOuter) return ZBank!.Value;
                return ZBank!.Value + (xLeftBedOuter - x) / MFp;
            }
            else
            {
                double xRightBedOuter = RightFloodplainLimit + BFpRight;
                if (x <= xRightBedOuter) return ZBank!.Value;
                return ZBank!.Value + (x - xRightBedOuter) / MFp;
            }
        }
    }
}
