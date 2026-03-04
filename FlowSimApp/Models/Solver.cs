using System;
using ClosedXML.Excel;

namespace FlowSim.Models
{
    /// <summary>Abstract base class for Saint-Venant equation solvers.</summary>
    public abstract class Solver
    {
        public Channel Channel { get; }
        public double TimeStep { get; protected set; }
        public double SpatialStep { get; protected set; }
        public int NumberOfNodes { get; protected set; }
        public int NumberOfTimeLevels { get; protected set; }
        public int TimeLevel { get; protected set; }
        public double NumCelerity { get; protected set; }
        public bool Solved { get; private set; }

        // Results
        public double[,]? Flow { get; protected set; }
        public double[,]? Depth { get; protected set; }
        public double[]? BedProfile { get; protected set; }
        public double[,]? Level { get; protected set; }
        public double[,]? Area { get; protected set; }
        public double[,]? TopWidth { get; protected set; }
        public double[,]? FroudeNumber { get; protected set; }
        public double[,]? Velocity { get; protected set; }
        public double[,]? WaveCelerity { get; protected set; }
        public double[,]? Amplitude { get; protected set; }
        public double[]? PeakAmplitude { get; protected set; }
        public double TotalSimDuration { get; private set; }

        protected readonly bool _fitSpatialStep;

        protected Solver(Channel channel, double timeStep, double spatialStep,
                         double simulationTime, bool fitSpatialStep = true)
        {
            Channel = channel;
            TimeStep = timeStep;
            SpatialStep = spatialStep;
            NumberOfNodes = (int)(channel.Length / spatialStep) + 1;
            NumberOfTimeLevels = (int)(simulationTime / timeStep) + 1;
            _fitSpatialStep = fitSpatialStep;

            if (fitSpatialStep) FitSpatialStep();

            Channel.InitializeConditions(NumberOfNodes);
            NumCelerity = SpatialStep / TimeStep;

            Flow = new double[NumberOfTimeLevels, NumberOfNodes];
            Depth = new double[NumberOfTimeLevels, NumberOfNodes];
        }

        protected void FitSpatialStep()
        {
            NumberOfNodes = (int)Math.Round(Channel.Length / SpatialStep) + 1;
            SpatialStep = Channel.Length / (NumberOfNodes - 1);
        }

        public abstract void Run(int verbose = 1);

        protected void InitializeT0()
        {
            for (int i = 0; i < NumberOfNodes; i++)
            {
                Depth![0, i] = Channel.InitialConditions![i, 0];
                Flow![0, i] = Channel.InitialConditions![i, 1];
            }
        }

        protected void PrepareResults()
        {
            int nk = TimeLevel + 1;

            if (nk < NumberOfTimeLevels)
            {
                var flowTrim = new double[nk, NumberOfNodes];
                var depthTrim = new double[nk, NumberOfNodes];
                for (int k = 0; k < nk; k++)
                    for (int i = 0; i < NumberOfNodes; i++)
                    { flowTrim[k, i] = Flow![k, i]; depthTrim[k, i] = Depth![k, i]; }
                Flow = flowTrim; Depth = depthTrim;
            }

            BedProfile = new double[NumberOfNodes];
            for (int i = 0; i < NumberOfNodes; i++)
                BedProfile[i] = Channel.XsAtNode![i].ZMin;

            Level = new double[nk, NumberOfNodes];
            for (int k = 0; k < nk; k++)
                for (int i = 0; i < NumberOfNodes; i++)
                    Level[k, i] = Depth![k, i] + BedProfile[i];

            Area = new double[nk, NumberOfNodes];
            TopWidth = new double[nk, NumberOfNodes];
            FroudeNumber = new double[nk, NumberOfNodes];

            for (int k = 0; k < nk; k++)
                for (int i = 0; i < NumberOfNodes; i++)
                {
                    double hw = Level![k, i];
                    Area![k, i] = Channel.AreaAt(i, hw);
                    TopWidth![k, i] = Channel.TopWidth(i, hw);
                    double A = Area[k, i], T = TopWidth[k, i], Q = Flow![k, i];
                    FroudeNumber![k, i] = Hydraulics.FroudeNumber(T, A, Q);
                }

            Velocity = new double[nk, NumberOfNodes];
            WaveCelerity = new double[nk, NumberOfNodes];
            for (int k = 0; k < nk; k++)
                for (int i = 0; i < NumberOfNodes; i++)
                {
                    double A = Area![k, i];
                    double T = TopWidth![k, i];
                    Velocity![k, i] = A > 1e-10 ? Flow![k, i] / A : 0;
                    WaveCelerity![k, i] = Velocity[k, i] + Math.Sqrt(Hydraulics.G * (T > 1e-10 ? A / T : 0));
                }

            Amplitude = new double[nk, NumberOfNodes];
            PeakAmplitude = new double[NumberOfNodes];
            for (int k = 0; k < nk; k++)
                for (int i = 0; i < NumberOfNodes; i++)
                    Amplitude![k, i] = Depth![k, i] - Depth[0, i];
            for (int i = 0; i < NumberOfNodes; i++)
            {
                double peak = 0;
                for (int k = 0; k < nk; k++)
                    peak = Math.Max(peak, Amplitude![k, i]);
                PeakAmplitude![i] = peak;
            }
        }

        protected void Finalize(int verbose)
        {
            Solved = true;
            TotalSimDuration = TimeLevel * TimeStep;
            PrepareResults();
            if (verbose >= 1) Console.WriteLine("Simulation completed successfully.");
        }

        public double DepthAt(int k, int i)
        {
            if (k < 0) k = TimeLevel + 1 + k;
            if (i < 0) i = NumberOfNodes + i;
            return Depth![k, i];
        }

        public double FlowAt(int k, int i)
        {
            if (k < 0) k = TimeLevel + 1 + k;
            if (i < 0) i = NumberOfNodes + i;
            return Flow![k, i];
        }

        public double WaterLevelAt(int k, int i)
        {
            if (i < 0) i = NumberOfNodes + i;
            return Channel.BedLevelAt(i) + DepthAt(k, i);
        }

        public double AreaAt(int k, int i)
        {
            if (i < 0) i = NumberOfNodes + i;
            return Channel.AreaAt(i, WaterLevelAt(k, i));
        }

        public double SeAt(int k, int i)
        {
            if (i < 0) i = NumberOfNodes + i;
            return Channel.Se(DepthAt(k, i), FlowAt(k, i), i);
        }

        public double DADhAt(int k, int i)
        {
            if (i < 0) i = NumberOfNodes + i;
            return Channel.DArea_Dh(i, WaterLevelAt(k, i));
        }

        public void SaveResults(string folderPath, string? fileName = null)
        {
            System.IO.Directory.CreateDirectory(folderPath);
            fileName ??= "results.xlsx";
            string filePath = System.IO.Path.Combine(folderPath, fileName);

            int nk = TimeLevel + 1;
            double[] distance = Channel.ChAtNode!;

            using var wb = new XLWorkbook();

            void WriteSheet(string name, double[,] arr)
            {
                var ws = wb.Worksheets.Add(name);
                ws.Cell(1, 1).Value = "Time\\Distance";
                for (int i = 0; i < distance.Length; i++) ws.Cell(1, i + 2).Value = distance[i];
                for (int k = 0; k < nk; k++)
                {
                    ws.Cell(k + 2, 1).Value = k * TimeStep;
                    for (int i = 0; i < NumberOfNodes; i++) ws.Cell(k + 2, i + 2).Value = arr[k, i];
                }
            }

            WriteSheet("Level", Level!);
            WriteSheet("Flow", Flow!);
            WriteSheet("Depth", Depth!);
            WriteSheet("Velocity", Velocity!);
            WriteSheet("Area", Area!);
            WriteSheet("Top width", TopWidth!);
            WriteSheet("Wave celerity", WaveCelerity!);
            WriteSheet("Amplitude", Amplitude!);
            WriteSheet("Froude number", FroudeNumber!);

            var wsPeak = wb.Worksheets.Add("Peak amplitude");
            for (int i = 0; i < distance.Length; i++) wsPeak.Cell(1, i + 1).Value = distance[i];
            for (int i = 0; i < NumberOfNodes; i++) wsPeak.Cell(2, i + 1).Value = PeakAmplitude![i];

            var wsBed = wb.Worksheets.Add("Bed level");
            for (int i = 0; i < distance.Length; i++) wsBed.Cell(1, i + 1).Value = distance[i];
            for (int i = 0; i < NumberOfNodes; i++) wsBed.Cell(2, i + 1).Value = BedProfile![i];

            wb.SaveAs(filePath);

            string txtPath = filePath.Replace(".xlsx", ".txt");
            using var sw = new System.IO.StreamWriter(txtPath);
            sw.WriteLine($"Spatial step = {SpatialStep} m");
            sw.WriteLine($"Time step = {TimeStep} s");

            double peakIn = 0, peakOut = 0, sumQin = 0;
            double massImbalance = 0;
            for (int k = 0; k < nk; k++)
            {
                double qIn = Flow![k, 0];
                double qOut = Flow![k, NumberOfNodes - 1];
                massImbalance += (qIn - qOut) * TimeStep;
                sumQin += qIn;
                peakIn = Math.Max(peakIn, qIn);
                peakOut = Math.Max(peakOut, qOut);
            }
            // massImbalance is total volume (m^3); total inflow volume = sumQin * TimeStep
            double totalInflowVolume = sumQin * TimeStep;
            double massImbPct = totalInflowVolume > 0 ? massImbalance / totalInflowVolume * 100 : 0;
            sw.WriteLine($"Mass imbalance = {massImbalance:F2} m^3 = {massImbPct:F4}% of inflow.");
            sw.WriteLine($"Peak inflow = {peakIn:F2} m^3/s");
            sw.WriteLine($"Peak outflow = {peakOut:F2} m^3/s");
            if (peakIn > 0) sw.WriteLine($"Attenuation = {(peakIn - peakOut) / peakIn * 100:F2}%");
        }
    }
}
