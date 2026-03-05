using System;

namespace HydroModel
{
    /// <summary>
    /// Boundary condition types.
    /// </summary>
    public enum BoundaryCondition
    {
        FlowHydrograph,
        FixedDepth,
        NormalDepth,
        RatingCurve,
        StageHydrograph
    }

    /// <summary>
    /// Represents a channel boundary (upstream or downstream).
    /// Equivalent to boundary.py.
    /// </summary>
    public class Boundary
    {
        public BoundaryCondition Condition { get; }
        public double Chainage { get; }
        public double? BedLevel { get; }
        public double? InitialDepth { get; private set; }
        public double? InitialStage { get; private set; }

        public CrossSection? CrossSection { get; set; }
        public RatingCurve? RatingCurveData { get; }
        public Hydrograph? HydrographData { get; }
        public LumpedStorage? LumpedStorage { get; private set; }

        public Boundary(string condition, double chainage, double? bedLevel = null,
            double? initialDepth = null, RatingCurve? ratingCurve = null,
            Hydrograph? hydrograph = null)
        {
            Condition = ParseCondition(condition);
            Chainage = chainage;
            BedLevel = bedLevel;

            if (initialDepth == null)
            {
                InitialDepth = null;
                InitialStage = null;
            }
            else
            {
                InitialDepth = initialDepth;
                InitialStage = bedLevel + initialDepth;
            }

            RatingCurveData = ratingCurve;
            HydrographData = hydrograph;
        }

        private static BoundaryCondition ParseCondition(string condition) => condition switch
        {
            "flow_hydrograph" => BoundaryCondition.FlowHydrograph,
            "fixed_depth" => BoundaryCondition.FixedDepth,
            "normal_depth" => BoundaryCondition.NormalDepth,
            "rating_curve" => BoundaryCondition.RatingCurve,
            "stage_hydrograph" => BoundaryCondition.StageHydrograph,
            _ => throw new ArgumentException($"Invalid boundary condition: {condition}")
        };

        /// <summary>Connects the boundary to a lumped storage element.</summary>
        public void SetLumpedStorage(LumpedStorage lumpedStorage)
        {
            LumpedStorage = lumpedStorage;
        }

        /// <summary>
        /// Computes the residual of the boundary condition equation.
        /// </summary>
        public double ConditionResidual(double depth, double flow, double? time = null,
            double? duration = null, double? volIn = null)
        {
            if (CrossSection == null)
                throw new InvalidOperationException("CrossSection not set on boundary.");

            double hw = CrossSection.ZMin + depth;
            double s0 = CrossSection.BedSlope ?? 0;

            double unknown = ConditionType() ? flow : depth;

            double target;

            switch (Condition)
            {
                case BoundaryCondition.FlowHydrograph:
                    if (time == null) throw new ArgumentException("Time required for flow_hydrograph.");
                    target = HydrographData!.GetAt(time.Value);
                    break;

                case BoundaryCondition.NormalDepth:
                    target = Hydraulics.NormalFlow(s0, k: CrossSection.Conveyance(hw));
                    break;

                case BoundaryCondition.RatingCurve:
                    target = RatingCurveData!.Discharge(BedLevel!.Value + depth, time);
                    break;

                case BoundaryCondition.FixedDepth:
                    if (LumpedStorage == null)
                    {
                        target = InitialDepth!.Value;
                    }
                    else
                    {
                        if (duration == null || volIn == null || time == null)
                            throw new ArgumentException("duration, volIn, and time required.");

                        int k = (int)(time.Value / duration.Value);
                        double yOld = k == 1 ? depth + BedLevel!.Value
                            : LumpedStorage.StageHydrograph[k - 2][1];

                        double reservoirStage = LumpedStorage.MassBalance(duration.Value, volIn.Value, yOld, time);

                        double R = CrossSection.HydraulicRadius(hw);
                        double n = CrossSection.GetEquivalentN(hw);
                        double area = CrossSection.Area(hw);
                        double headLoss = LumpedStorage.EnergyLoss(area, flow, n, R);

                        double interfaceStage = reservoirStage + headLoss;

                        if (LumpedStorage.StageHydrograph.Count == 0)
                            LumpedStorage.StageHydrograph.Add(new[] { time.Value, reservoirStage });
                        else if (LumpedStorage.StageHydrograph[^1][0] == time.Value)
                            LumpedStorage.StageHydrograph[^1][1] = reservoirStage;
                        else
                            LumpedStorage.StageHydrograph.Add(new[] { time.Value, reservoirStage });

                        target = interfaceStage - BedLevel!.Value;
                    }
                    break;

                case BoundaryCondition.StageHydrograph:
                    if (time == null) throw new ArgumentException("Time required for stage_hydrograph.");
                    target = HydrographData!.GetAt(time.Value) - BedLevel!.Value;
                    break;

                default:
                    throw new InvalidOperationException("Unknown boundary condition.");
            }

            return unknown - target;
        }

        /// <summary>
        /// Derivative of boundary condition residual w.r.t. depth (df/dh).
        /// </summary>
        public double DfDh(double depth, double flowRate, double? time = null)
        {
            if (CrossSection == null)
                throw new InvalidOperationException("CrossSection not set on boundary.");

            if (Condition == BoundaryCondition.FlowHydrograph)
                return 0;

            double hw = depth + BedLevel!.Value;
            double s0 = CrossSection.BedSlope ?? 0;
            double dAdH = CrossSection.DAreaDDepth(hw);

            switch (Condition)
            {
                case BoundaryCondition.FixedDepth:
                    if (LumpedStorage != null)
                    {
                        double R = CrossSection.HydraulicRadius(hw);
                        double n = CrossSection.GetEquivalentN(hw);
                        double dRdA = CrossSection.DHydraulicRadiusDArea(hw);
                        double area = CrossSection.Area(hw);
                        double dHlDa = LumpedStorage.DEnergyLossDArea(area, flowRate, n, R, dRdA);
                        return 1.0 - dHlDa * dAdH;
                    }
                    return 1.0;

                case BoundaryCondition.NormalDepth:
                    return 0 - Hydraulics.DNormalFlowDArea(s0,
                        dKdA: CrossSection.DConveyanceDArea(hw)) * dAdH;

                case BoundaryCondition.RatingCurve:
                    return 0 - RatingCurveData!.DischargeDStage(BedLevel!.Value + depth, time);

                case BoundaryCondition.StageHydrograph:
                    return 1.0;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Derivative of boundary condition residual w.r.t. flow rate (df/dQ).
        /// </summary>
        public double DfDQ(double depth, double flowRate, double? duration = null,
            double? time = null, double? volIn = null)
        {
            if (ConditionType())
                return 1.0;

            if (CrossSection == null)
                throw new InvalidOperationException("CrossSection not set on boundary.");

            double hw = depth + BedLevel!.Value;

            switch (Condition)
            {
                case BoundaryCondition.FixedDepth:
                    if (LumpedStorage != null)
                    {
                        if (duration == null || time == null || volIn == null)
                            throw new ArgumentException("duration, time, volIn required.");

                        int k = (int)(time.Value / duration.Value);
                        double yOld = k == 1 ? depth + BedLevel.Value
                            : LumpedStorage.StageHydrograph[k - 2][1];

                        double dYNewDVol = LumpedStorage.DYNewDVolIn(duration.Value, volIn.Value, yOld, time);
                        double dVolDQ = 0.5 * duration.Value;

                        double R = CrossSection.HydraulicRadius(hw);
                        double n = CrossSection.GetEquivalentN(hw);
                        double area = CrossSection.Area(hw);
                        double dHlDQ = LumpedStorage.DEnergyLossDFlow(area, flowRate, n, R);

                        return 0 - (dYNewDVol * dVolDQ + dHlDQ);
                    }
                    return 0;

                case BoundaryCondition.StageHydrograph:
                    return 0;

                default:
                    return 0;
            }
        }

        /// <summary>
        /// Returns true if the boundary equation depends on Q (flow).
        /// </summary>
        public bool ConditionType()
        {
            return Condition == BoundaryCondition.FlowHydrograph
                   || Condition == BoundaryCondition.NormalDepth
                   || Condition == BoundaryCondition.RatingCurve;
        }
    }
}
