using System;

namespace FlowSim.Models
{
    public enum BoundaryConditionType
    {
        FlowHydrograph,
        FixedDepth,
        NormalDepth,
        RatingCurve,
        StageHydrograph
    }

    /// <summary>Represents an upstream or downstream boundary condition.</summary>
    public class Boundary
    {
        public BoundaryConditionType Condition { get; }
        public CrossSection? CrossSection { get; set; }
        public double? BedLevel { get; }
        public double? InitialDepth { get; set; }
        public double? InitialStage { get; }
        public double Chainage { get; }
        public RatingCurve? RatingCurve { get; }
        public Hydrograph? Hydrograph { get; }
        public LumpedStorage? LumpedStorage { get; private set; }

        public Boundary(BoundaryConditionType condition, double chainage,
                        double? bedLevel = null, double? initialDepth = null,
                        RatingCurve? ratingCurve = null, Hydrograph? hydrograph = null)
        {
            Condition = condition;
            Chainage = chainage;
            BedLevel = bedLevel;
            InitialDepth = initialDepth;
            InitialStage = bedLevel.HasValue && initialDepth.HasValue ? bedLevel + initialDepth : null;
            RatingCurve = ratingCurve;
            Hydrograph = hydrograph;
        }

        public void SetLumpedStorage(LumpedStorage ls) => LumpedStorage = ls;

        /// <summary>Returns true if the boundary equation depends on Q (flow), false if on depth.</summary>
        public bool IsFlowDependent =>
            Condition == BoundaryConditionType.FlowHydrograph ||
            Condition == BoundaryConditionType.NormalDepth ||
            Condition == BoundaryConditionType.RatingCurve;

        public double ConditionResidual(double depth, double flow, double time = 0,
                                        double duration = 0, double volIn = 0)
        {
            if (CrossSection == null) throw new InvalidOperationException("Cross section not set.");
            double hw = CrossSection.ZMin + depth;
            double S0 = CrossSection.BedSlope ?? 0;

            double unknown = IsFlowDependent ? flow : depth;
            double target;

            switch (Condition)
            {
                case BoundaryConditionType.FlowHydrograph:
                    if (Hydrograph == null) throw new InvalidOperationException("Hydrograph not set.");
                    target = Hydrograph.GetAt(time);
                    break;

                case BoundaryConditionType.NormalDepth:
                    target = Hydraulics.NormalFlow(S0, CrossSection.Conveyance(hw));
                    break;

                case BoundaryConditionType.RatingCurve:
                    if (RatingCurve == null) throw new InvalidOperationException("Rating curve not set.");
                    target = RatingCurve.Discharge((BedLevel ?? 0) + depth, time);
                    break;

                case BoundaryConditionType.FixedDepth:
                    if (LumpedStorage == null)
                    {
                        target = InitialDepth ?? 0;
                    }
                    else
                    {
                        if (duration <= 0) throw new ArgumentException("Duration must be positive for lumped storage boundary.");
                        int k = (int)(time / duration);
                        double yOld;
                        if (k == 1)
                            yOld = depth + (BedLevel ?? 0);
                        else if (k >= 2 && LumpedStorage.StageHydrograph.Count >= k - 1)
                            yOld = LumpedStorage.StageHydrograph[k - 2][1];
                        else
                            yOld = depth + (BedLevel ?? 0);

                        double reservoirStage = LumpedStorage.MassBalance(duration, volIn, yOld, time);
                        double n = CrossSection.GetEquivalentN(hw);
                        double R = CrossSection.HydraulicRadius(hw);
                        double area = CrossSection.Area(hw);
                        double headLoss = LumpedStorage.EnergyLoss(area, flow, n, R);
                        double interfaceStage = reservoirStage + headLoss;

                        if (LumpedStorage.StageHydrograph.Count == 0)
                            LumpedStorage.StageHydrograph.Add(new[] { time, reservoirStage });
                        else if (Math.Abs(LumpedStorage.StageHydrograph[^1][0] - time) < 1e-9)
                            LumpedStorage.StageHydrograph[^1][1] = reservoirStage;
                        else
                            LumpedStorage.StageHydrograph.Add(new[] { time, reservoirStage });

                        target = interfaceStage - (BedLevel ?? 0);
                    }
                    break;

                case BoundaryConditionType.StageHydrograph:
                    if (Hydrograph == null) throw new InvalidOperationException("Hydrograph not set.");
                    target = Hydrograph.GetAt(time) - (BedLevel ?? 0);
                    break;

                default:
                    throw new InvalidOperationException("Invalid boundary condition type.");
            }

            return unknown - target;
        }

        public double Df_Dh(double depth, double flowRate, double time = 0)
        {
            if (Condition == BoundaryConditionType.FlowHydrograph) return 0;
            if (CrossSection == null) throw new InvalidOperationException("Cross section not set.");

            double hw = depth + (BedLevel ?? CrossSection.ZMin);
            double dA_dh = CrossSection.DArea_Dh(hw);
            double S0 = CrossSection.BedSlope ?? 0;

            switch (Condition)
            {
                case BoundaryConditionType.FixedDepth:
                    if (LumpedStorage != null)
                    {
                        double R = CrossSection.HydraulicRadius(hw);
                        double n = CrossSection.GetEquivalentN(hw);
                        double dR_dA = CrossSection.DRadius_DA(hw);
                        double area = CrossSection.Area(hw);
                        double dhl_dA = LumpedStorage.Dhl_DA(area, flowRate, n, R, dR_dA);
                        return 1.0 - dhl_dA * dA_dh;
                    }
                    return 1.0;

                case BoundaryConditionType.NormalDepth:
                    double dK_dA = CrossSection.DConveyance_DA(hw);
                    return -Hydraulics.DNormalFlow_DA(S0, dK_dA) * dA_dh;

                case BoundaryConditionType.RatingCurve:
                    if (RatingCurve == null) return 0;
                    return -RatingCurve.DQ_Dz((BedLevel ?? 0) + depth, time);

                case BoundaryConditionType.StageHydrograph:
                    return 1.0;

                default:
                    return 0;
            }
        }

        public double Df_DQ(double depth, double flowRate, double duration = 0, double time = 0, double volIn = 0)
        {
            if (IsFlowDependent) return 1.0;

            if (CrossSection == null) throw new InvalidOperationException("Cross section not set.");
            double hw = depth + (BedLevel ?? CrossSection.ZMin);

            switch (Condition)
            {
                case BoundaryConditionType.FixedDepth:
                    if (LumpedStorage != null)
                    {
                        int k = (int)(time / Math.Max(duration, 1e-10));
                        double yOld;
                        if (k == 1 || LumpedStorage.StageHydrograph.Count < k - 1)
                            yOld = depth + (BedLevel ?? 0);
                        else
                            yOld = LumpedStorage.StageHydrograph[k - 2][1];

                        double dY_dvol = LumpedStorage.DYnew_DvolIn(duration, volIn, yOld, time);
                        double dvol_dQ = 0.5 * duration;
                        double R = CrossSection.HydraulicRadius(hw);
                        double n = CrossSection.GetEquivalentN(hw);
                        double area = CrossSection.Area(hw);
                        double dhl_dQ = LumpedStorage.Dhl_DQ(area, flowRate, n, R);
                        return -(dY_dvol * dvol_dQ + dhl_dQ);
                    }
                    return 0;

                case BoundaryConditionType.StageHydrograph:
                    return 0;

                default:
                    return 0;
            }
        }
    }
}
