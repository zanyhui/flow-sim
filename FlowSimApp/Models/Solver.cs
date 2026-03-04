using System;
using ClosedXML.Excel;

namespace FlowSim.Models
{
    /// <summary>
    /// 圣维南方程组求解器的抽象基类。
    /// <para>
    /// 封装了所有求解器共享的功能：
    /// <list type="bullet">
    ///   <item>存储河道对象和仿真参数（时间步长、空间步长、节点数等）；</item>
    ///   <item>提供流量、水深、水位等结果数组的存储空间；</item>
    ///   <item>提供 <see cref="InitializeT0"/>（t=0 初始化）和 <see cref="PrepareResults"/>（结果后处理）；</item>
    ///   <item>提供 <see cref="SaveResults"/> 将计算结果写入 Excel 和文本文件；</item>
    ///   <item>提供快捷访问方法 <see cref="DepthAt"/>、<see cref="FlowAt"/> 等（支持负索引）。</item>
    /// </list>
    /// 子类须实现 <see cref="Run"/> 方法，执行具体的时间推进格式（显式或隐式）。
    /// </para>
    /// </summary>
    public abstract class Solver
    {
        /// <summary>关联的河道对象（包含断面、边界等信息）。</summary>
        public Channel Channel { get; }

        /// <summary>时间步长 Δt（秒）。</summary>
        public double TimeStep { get; protected set; }

        /// <summary>空间步长 Δx（m），可能在 FitSpatialStep 后被微调。</summary>
        public double SpatialStep { get; protected set; }

        /// <summary>计算节点总数 N（含上下游端节点）。</summary>
        public int NumberOfNodes { get; protected set; }

        /// <summary>总时间层数（时步数 + 1）。</summary>
        public int NumberOfTimeLevels { get; protected set; }

        /// <summary>当前已完成的时间层索引（从 0 开始），仿真完成后等于 NumberOfTimeLevels - 1。</summary>
        public int TimeLevel { get; protected set; }

        /// <summary>数值波速 Δx/Δt（m/s），用于 CFL 条件检查（Lax 格式使用）。</summary>
        public double NumCelerity { get; protected set; }

        /// <summary>仿真是否已成功完成。</summary>
        public bool Solved { get; private set; }

        // ---- 结果数组（仿真结束后由 PrepareResults 填充）----
        /// <summary>流量数组 [时间层, 节点]（m³/s）。</summary>
        public double[,]? Flow { get; protected set; }
        /// <summary>水深数组 [时间层, 节点]（m）。</summary>
        public double[,]? Depth { get; protected set; }
        /// <summary>床底高程纵剖面数组 [节点]（m）。</summary>
        public double[]? BedProfile { get; protected set; }
        /// <summary>绝对水位数组 [时间层, 节点]（m）= Depth + BedProfile。</summary>
        public double[,]? Level { get; protected set; }
        /// <summary>过水面积数组 [时间层, 节点]（m²）。</summary>
        public double[,]? Area { get; protected set; }
        /// <summary>水面宽数组 [时间层, 节点]（m）。</summary>
        public double[,]? TopWidth { get; protected set; }
        /// <summary>弗劳德数数组 [时间层, 节点]（无量纲）。</summary>
        public double[,]? FroudeNumber { get; protected set; }
        /// <summary>断面平均流速数组 [时间层, 节点]（m/s）。</summary>
        public double[,]? Velocity { get; protected set; }
        /// <summary>波速（V + sqrt(gD)）数组 [时间层, 节点]（m/s）。</summary>
        public double[,]? WaveCelerity { get; protected set; }
        /// <summary>水深变化（涌波振幅）数组 [时间层, 节点]（m）= Depth - Depth[0,:]。</summary>
        public double[,]? Amplitude { get; protected set; }
        /// <summary>各节点峰值振幅数组 [节点]（m）。</summary>
        public double[]? PeakAmplitude { get; protected set; }
        /// <summary>实际模拟总时长（秒）= TimeLevel * TimeStep。</summary>
        public double TotalSimDuration { get; private set; }

        /// <summary>是否自动调整空间步长以整除河道长度（默认 true）。</summary>
        protected readonly bool _fitSpatialStep;

        /// <summary>
        /// 构造求解器基类，初始化参数并为河道初始化条件。
        /// </summary>
        /// <param name="channel">河道对象。</param>
        /// <param name="timeStep">时间步长（秒）。</param>
        /// <param name="spatialStep">目标空间步长（m）。</param>
        /// <param name="simulationTime">总模拟时长（秒）。</param>
        /// <param name="fitSpatialStep">是否微调空间步长使其整除河道长度（默认 true）。</param>
        protected Solver(Channel channel, double timeStep, double spatialStep,
                         double simulationTime, bool fitSpatialStep = true)
        {
            Channel = channel;
            TimeStep = timeStep;
            SpatialStep = spatialStep;

            // 计算节点数和时间层数（均取整后 +1 以包含端点）
            NumberOfNodes = (int)(channel.Length / spatialStep) + 1;
            NumberOfTimeLevels = (int)(simulationTime / timeStep) + 1;
            _fitSpatialStep = fitSpatialStep;

            // 微调空间步长，使节点均匀覆盖河道全长
            if (fitSpatialStep) FitSpatialStep();

            // 初始化河道各节点的断面和初始条件
            Channel.InitializeConditions(NumberOfNodes);

            // 数值波速（= Δx/Δt），用于 CFL 检查
            NumCelerity = SpatialStep / TimeStep;

            // 分配主要结果数组（Flow 和 Depth），其余在 PrepareResults 中分配
            Flow  = new double[NumberOfTimeLevels, NumberOfNodes];
            Depth = new double[NumberOfTimeLevels, NumberOfNodes];
        }

        /// <summary>
        /// 微调空间步长，使节点数为整数且均匀覆盖河道长度。
        /// 四舍五入后重新计算实际步长：Δx = L / (N-1)。
        /// </summary>
        protected void FitSpatialStep()
        {
            NumberOfNodes = (int)Math.Round(Channel.Length / SpatialStep) + 1;
            SpatialStep = Channel.Length / (NumberOfNodes - 1);
        }

        /// <summary>
        /// 执行仿真，推进所有时间层。子类须实现此方法。
        /// </summary>
        /// <param name="verbose">
        /// 输出详细程度：0=静默，1=每时步输出进度，2=输出迭代次数，3=输出每步残差。
        /// </param>
        public abstract void Run(int verbose = 1);

        /// <summary>
        /// 将初始条件（t=0）赋值到 Depth[0,:] 和 Flow[0,:]。
        /// 子类在构造完成后调用此方法设置起始状态。
        /// </summary>
        protected void InitializeT0()
        {
            for (int i = 0; i < NumberOfNodes; i++)
            {
                Depth![0, i] = Channel.InitialConditions![i, 0];  // 初始水深
                Flow![0, i]  = Channel.InitialConditions![i, 1];  // 初始流量
            }
        }

        /// <summary>
        /// 仿真完成后进行结果后处理：
        /// 1. 裁剪结果数组到实际时间层数（若提前终止）；
        /// 2. 计算床底纵剖面 BedProfile；
        /// 3. 由水深 + 床底高程计算绝对水位 Level；
        /// 4. 计算过水面积 Area、水面宽 TopWidth、弗劳德数 FroudeNumber；
        /// 5. 计算断面平均流速 Velocity 和波速 WaveCelerity；
        /// 6. 计算涌波振幅 Amplitude 和各节点峰值振幅 PeakAmplitude。
        /// </summary>
        protected void PrepareResults()
        {
            int nk = TimeLevel + 1;   // 实际完成的时间层数

            // 若仿真提前结束（实际时步 < 预设时步），裁剪数组节省内存
            if (nk < NumberOfTimeLevels)
            {
                var flowTrim  = new double[nk, NumberOfNodes];
                var depthTrim = new double[nk, NumberOfNodes];
                for (int k = 0; k < nk; k++)
                    for (int i = 0; i < NumberOfNodes; i++)
                    { flowTrim[k, i] = Flow![k, i]; depthTrim[k, i] = Depth![k, i]; }
                Flow = flowTrim; Depth = depthTrim;
            }

            // 床底纵剖面（各节点 ZMin）
            BedProfile = new double[NumberOfNodes];
            for (int i = 0; i < NumberOfNodes; i++)
                BedProfile[i] = Channel.XsAtNode![i].ZMin;

            // 绝对水位 = 水深 + 床底高程
            Level = new double[nk, NumberOfNodes];
            for (int k = 0; k < nk; k++)
                for (int i = 0; i < NumberOfNodes; i++)
                    Level[k, i] = Depth![k, i] + BedProfile[i];

            // 过水面积、水面宽、弗劳德数
            Area        = new double[nk, NumberOfNodes];
            TopWidth    = new double[nk, NumberOfNodes];
            FroudeNumber = new double[nk, NumberOfNodes];

            for (int k = 0; k < nk; k++)
                for (int i = 0; i < NumberOfNodes; i++)
                {
                    double hw = Level![k, i];
                    Area![k, i]     = Channel.AreaAt(i, hw);
                    TopWidth![k, i] = Channel.TopWidth(i, hw);
                    double A = Area[k, i], T = TopWidth[k, i], Q = Flow![k, i];
                    FroudeNumber![k, i] = Hydraulics.FroudeNumber(T, A, Q);
                }

            // 断面平均流速 V = Q/A；波速 c_wave = V + sqrt(gD)
            Velocity    = new double[nk, NumberOfNodes];
            WaveCelerity = new double[nk, NumberOfNodes];
            for (int k = 0; k < nk; k++)
                for (int i = 0; i < NumberOfNodes; i++)
                {
                    double A = Area![k, i];
                    double T = TopWidth![k, i];
                    Velocity![k, i]     = A > 1e-10 ? Flow![k, i] / A : 0;  // 防止除零
                    WaveCelerity![k, i] = Velocity[k, i] + Math.Sqrt(Hydraulics.G * (T > 1e-10 ? A / T : 0));
                }

            // 涌波振幅 = 水深 - 初始水深
            Amplitude     = new double[nk, NumberOfNodes];
            PeakAmplitude = new double[NumberOfNodes];
            for (int k = 0; k < nk; k++)
                for (int i = 0; i < NumberOfNodes; i++)
                    Amplitude![k, i] = Depth![k, i] - Depth[0, i];   // 相对于 t=0 的变化量

            // 各节点历史最大振幅
            for (int i = 0; i < NumberOfNodes; i++)
            {
                double peak = 0;
                for (int k = 0; k < nk; k++)
                    peak = Math.Max(peak, Amplitude![k, i]);
                PeakAmplitude![i] = peak;
            }
        }

        /// <summary>
        /// 仿真结束时调用：标记 Solved=true，记录实际模拟时长，调用 PrepareResults。
        /// </summary>
        /// <param name="verbose">输出级别（>= 1 时在控制台打印完成信息）。</param>
        protected void Finalize(int verbose)
        {
            Solved = true;
            TotalSimDuration = TimeLevel * TimeStep;    // 实际模拟总时长
            PrepareResults();
            if (verbose >= 1) Console.WriteLine("Simulation completed successfully.");
        }

        // ---- 带负索引支持的快捷访问方法（负值表示从末尾倒数）----

        /// <summary>获取时间层 k、节点 i 处的水深（支持负索引）。</summary>
        public double DepthAt(int k, int i)
        {
            if (k < 0) k = TimeLevel + 1 + k;
            if (i < 0) i = NumberOfNodes + i;
            return Depth![k, i];
        }

        /// <summary>获取时间层 k、节点 i 处的流量（支持负索引）。</summary>
        public double FlowAt(int k, int i)
        {
            if (k < 0) k = TimeLevel + 1 + k;
            if (i < 0) i = NumberOfNodes + i;
            return Flow![k, i];
        }

        /// <summary>获取时间层 k、节点 i 处的绝对水位 = 床底 + 水深（支持负索引）。</summary>
        public double WaterLevelAt(int k, int i)
        {
            if (i < 0) i = NumberOfNodes + i;
            return Channel.BedLevelAt(i) + DepthAt(k, i);
        }

        /// <summary>获取时间层 k、节点 i 处的过水面积（支持负索引）。</summary>
        public double AreaAt(int k, int i)
        {
            if (i < 0) i = NumberOfNodes + i;
            return Channel.AreaAt(i, WaterLevelAt(k, i));
        }

        /// <summary>获取时间层 k、节点 i 处的等效能量坡度 Se（支持负索引）。</summary>
        public double SeAt(int k, int i)
        {
            if (i < 0) i = NumberOfNodes + i;
            return Channel.Se(DepthAt(k, i), FlowAt(k, i), i);
        }

        /// <summary>获取时间层 k、节点 i 处的 dA/dh（水面宽，支持负索引）。</summary>
        public double DADhAt(int k, int i)
        {
            if (i < 0) i = NumberOfNodes + i;
            return Channel.DArea_Dh(i, WaterLevelAt(k, i));
        }

        /// <summary>
        /// 将仿真结果保存为 Excel（.xlsx）文件，并附带摘要文本文件（.txt）。
        /// <para>
        /// Excel 中各工作表分别存储：水位、流量、水深、流速、面积、水面宽、
        /// 波速、振幅、弗劳德数、峰值振幅、床底高程。
        /// 行对应时间层（第 1 行为节点桩号），列对应节点。
        /// </para>
        /// </summary>
        /// <param name="folderPath">输出文件夹路径（不存在时自动创建）。</param>
        /// <param name="fileName">输出文件名（默认 "results.xlsx"）。</param>
        public void SaveResults(string folderPath, string? fileName = null)
        {
            System.IO.Directory.CreateDirectory(folderPath);
            fileName ??= "results.xlsx";
            string filePath = System.IO.Path.Combine(folderPath, fileName);

            int nk = TimeLevel + 1;
            double[] distance = Channel.ChAtNode!;

            using var wb = new XLWorkbook();

            // 局部函数：向工作表写入二维数组（行 = 时间层，列 = 节点）
            void WriteSheet(string name, double[,] arr)
            {
                var ws = wb.Worksheets.Add(name);
                ws.Cell(1, 1).Value = "Time\\Distance";      // 左上角标题
                for (int i = 0; i < distance.Length; i++)
                    ws.Cell(1, i + 2).Value = distance[i];   // 第 1 行：各节点桩号（m）
                for (int k = 0; k < nk; k++)
                {
                    ws.Cell(k + 2, 1).Value = k * TimeStep;  // 第 1 列：时间（秒）
                    for (int i = 0; i < NumberOfNodes; i++)
                        ws.Cell(k + 2, i + 2).Value = arr[k, i];
                }
            }

            // 写入各物理量工作表
            WriteSheet("Level",       Level!);
            WriteSheet("Flow",        Flow!);
            WriteSheet("Depth",       Depth!);
            WriteSheet("Velocity",    Velocity!);
            WriteSheet("Area",        Area!);
            WriteSheet("Top width",   TopWidth!);
            WriteSheet("Wave celerity", WaveCelerity!);
            WriteSheet("Amplitude",   Amplitude!);
            WriteSheet("Froude number", FroudeNumber!);

            // 峰值振幅（一维，仅有距离轴）
            var wsPeak = wb.Worksheets.Add("Peak amplitude");
            for (int i = 0; i < distance.Length; i++)
                wsPeak.Cell(1, i + 1).Value = distance[i];
            for (int i = 0; i < NumberOfNodes; i++)
                wsPeak.Cell(2, i + 1).Value = PeakAmplitude![i];

            // 床底高程纵剖面
            var wsBed = wb.Worksheets.Add("Bed level");
            for (int i = 0; i < distance.Length; i++)
                wsBed.Cell(1, i + 1).Value = distance[i];
            for (int i = 0; i < NumberOfNodes; i++)
                wsBed.Cell(2, i + 1).Value = BedProfile![i];

            wb.SaveAs(filePath);

            // 同时写入一个简洁的文本摘要文件
            string txtPath = filePath.Replace(".xlsx", ".txt");
            using var sw = new System.IO.StreamWriter(txtPath);
            sw.WriteLine($"Spatial step = {SpatialStep} m");
            sw.WriteLine($"Time step = {TimeStep} s");

            // 统计峰值流量和质量不平衡
            double peakIn = 0, peakOut = 0, sumQin = 0;
            double massImbalance = 0;
            for (int k = 0; k < nk; k++)
            {
                double qIn  = Flow![k, 0];                       // 上游入流量
                double qOut = Flow![k, NumberOfNodes - 1];       // 下游出流量
                massImbalance += (qIn - qOut) * TimeStep;        // 累计体积不平衡（m³）
                sumQin += qIn;
                peakIn  = Math.Max(peakIn, qIn);
                peakOut = Math.Max(peakOut, qOut);
            }
            // 质量不平衡百分比 = 体积不平衡 / 总入流体积 * 100%
            double totalInflowVolume = sumQin * TimeStep;
            double massImbPct = totalInflowVolume > 0 ? massImbalance / totalInflowVolume * 100 : 0;

            sw.WriteLine($"Mass imbalance = {massImbalance:F2} m^3 = {massImbPct:F4}% of inflow.");
            sw.WriteLine($"Peak inflow = {peakIn:F2} m^3/s");
            sw.WriteLine($"Peak outflow = {peakOut:F2} m^3/s");
            if (peakIn > 0)
                sw.WriteLine($"Attenuation = {(peakIn - peakOut) / peakIn * 100:F2}%");
        }
    }
}
