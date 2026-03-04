using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlowSim.Models;
using ScottPlot;

namespace FlowSim
{
    /// <summary>
    /// 主窗体：FlowSim 一维水动力仿真的图形用户界面。
    /// <para>
    /// 功能概述：
    /// <list type="bullet">
    ///   <item>通过"河道设置"、"边界条件"、"求解器设置"选项卡接受仿真参数输入；</item>
    ///   <item>点击"运行仿真"按钮，在后台线程中调用 Preissmann 或 Lax-Friedrichs 求解器；</item>
    ///   <item>仿真完成后，在"结果"选项卡展示流量过程线、水面纵剖面图以及统计汇总；</item>
    ///   <item>点击"保存结果"按钮，将计算结果导出为 Excel 和文本摘要文件。</item>
    /// </list>
    /// </para>
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary>当前仿真求解器实例（仿真前为 null）。</summary>
        private Solver? _solver;

        /// <summary>已加载的不规则断面横坐标数组（m）。</summary>
        private double[]? _xsX;
        /// <summary>已加载的不规则断面高程数组（m）。</summary>
        private double[]? _xsZ;

        /// <summary>
        /// 构造函数：调用 WinForms 生成的控件初始化代码。
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
        }

        /// <summary>
        /// "运行仿真"按钮点击事件处理器。
        /// <para>
        /// 步骤：
        /// 1. 禁用按钮（防止重复点击）；
        /// 2. 调用 <see cref="BuildSolver"/> 根据界面参数构造求解器；
        /// 3. 在后台任务（Task.Run）中执行仿真（避免冻结 UI 线程）；
        /// 4. 仿真结束后切回 UI 线程更新结果图表和统计汇总。
        /// </para>
        /// </summary>
        private void btnRun_Click(object sender, EventArgs e)
        {
            btnRun.Enabled  = false;   // 仿真期间禁止再次点击
            btnSave.Enabled = false;
            txtLog.Clear();
            Log("正在构建仿真...");

            try
            {
                var solver = BuildSolver();   // 根据 UI 参数构造求解器
                _solver = solver;

                Log("正在运行仿真...");
                // 在后台线程运行仿真，保持 UI 响应
                Task.Run(() =>
                {
                    try
                    {
                        _solver.Run(verbose: 1);
                        // 仿真完成后回到 UI 线程更新界面
                        Invoke(() =>
                        {
                            Log("仿真成功完成。");
                            UpdateResults();
                            btnSave.Enabled = true;
                            btnRun.Enabled  = true;
                        });
                    }
                    catch (Exception ex)
                    {
                        // 仿真过程中出现异常（如 CFL 条件违反）
                        Invoke(() =>
                        {
                            Log($"错误：{ex.Message}");
                            btnRun.Enabled = true;
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                // 求解器初始化失败（如参数无效）
                Log($"初始化错误：{ex.Message}");
                btnRun.Enabled = true;
            }
        }

        /// <summary>
        /// "保存结果"按钮点击事件处理器。
        /// 弹出文件夹选择对话框，调用 <see cref="Solver.SaveResults"/> 将结果写入 Excel 和文本文件。
        /// </summary>
        private void btnSave_Click(object sender, EventArgs e)
        {
            if (_solver == null || !_solver.Solved) return;
            using var dlg = new FolderBrowserDialog { Description = "选择输出文件夹" };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _solver.SaveResults(dlg.SelectedPath, "results.xlsx");
                    Log($"结果已保存至：{dlg.SelectedPath}");
                }
                catch (Exception ex)
                {
                    Log($"保存错误：{ex.Message}");
                }
            }
        }

        /// <summary>
        /// 加载不规则断面 CSV 文件（格式：首行为标题，第 1 列横坐标，第 2 列高程）。
        /// 解析成功后将数据存入 <see cref="_xsX"/> 和 <see cref="_xsZ"/>。
        /// </summary>
        private void btnLoadXsCsv_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "选择不规则断面 CSV 文件",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var xs = new System.Collections.Generic.List<double>();
                var zs = new System.Collections.Generic.List<double>();
                bool skipHeader = true;
                foreach (var line in System.IO.File.ReadAllLines(dlg.FileName))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 2) continue;
                    // 首行若含非数字则视为标题行跳过
                    if (skipHeader && !double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out _))
                    { skipHeader = false; continue; }
                    skipHeader = false;
                    if (double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double x) &&
                        double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double z))
                    { xs.Add(x); zs.Add(z); }
                }
                if (xs.Count < 3)
                    throw new InvalidOperationException("断面数据不足（至少需要 3 个点）。");
                _xsX = xs.ToArray();
                _xsZ = zs.ToArray();
                lblXsFile.Text = System.IO.Path.GetFileName(dlg.FileName);
                Log($"已加载断面文件：{dlg.FileName}（{_xsX.Length} 个点）");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载断面 CSV 失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 集总调蓄库启用复选框变更事件：启用/禁用相关参数控件。
        /// </summary>
        private void chkLumpedStorage_CheckedChanged(object sender, EventArgs e)
        {
            bool en = chkLumpedStorage.Checked;
            numLsYMin.Enabled = numLsYMax.Enabled = numLsSurfaceArea.Enabled = cmbLsRcType.Enabled = en;
            numLsRcA.Enabled = numLsRcB.Enabled = numLsRcShift.Enabled = en && cmbLsRcType.SelectedIndex > 0;
        }

        /// <summary>
        /// 根据界面控件当前值构造一维河道和求解器。
        /// <para>
        /// 构造流程：
        /// 1. 读取河道几何参数（长度、宽度、糙率、床底高程等）；
        /// 2. 根据上游边界类型（流量过程线 or 正常水深）构造上游边界；
        ///    若选择流量过程线，则调用 <see cref="BuildTriangularHydrograph"/> 构造三角形洪水过程；
        /// 3. 根据下游边界类型（正常水深 or 固定水深）构造下游边界；
        /// 4. 构造 <see cref="Channel"/> 对象（使用 GVFEquation 初始化方式）；
        /// 5. 根据用户选择的格式（Preissmann or Lax-Friedrichs）构造求解器。
        /// </para>
        /// </summary>
        /// <returns>初始化完成的求解器实例（<see cref="PreissmannSolver"/> 或 <see cref="LaxSolver"/>）。</returns>
        private Solver BuildSolver()
        {
            // 读取河道几何参数
            double length      = (double)numLength.Value;
            double width       = (double)numWidth.Value;
            double roughness   = (double)numRoughness.Value;
            double initialFlow = (double)numInitialFlow.Value;
            double usBedLevel  = (double)numUsBedLevel.Value;
            double dsBedLevel  = (double)numDsBedLevel.Value;
            double dsDepth     = (double)numDsDepth.Value;

            // 根据上游边界类型选择
            Hydrograph? usHydrograph = null;
            BoundaryConditionType usBcType;
            switch (cmbUsBcType.SelectedIndex)
            {
                case 0:  // 流量过程线：三角形洪水过程
                    usBcType = BoundaryConditionType.FlowHydrograph;
                    usHydrograph = BuildTriangularHydrograph(
                        (double)numPeakFlow.Value,
                        (double)numRiseTime.Value * 3600,       // 小时转秒
                        (double)numSimTime.Value * 3600);
                    break;
                case 1:  // 正常水深边界：出口用均匀流公式
                    usBcType = BoundaryConditionType.NormalDepth;
                    break;
                default:
                    // 默认：恒定初始流量
                    usBcType     = BoundaryConditionType.FlowHydrograph;
                    usHydrograph = new Hydrograph(t => initialFlow);
                    break;
            }

            // 根据下游边界类型选择（若启用调蓄库则强制 FixedDepth）
            bool useLumpedStorage = chkLumpedStorage.Checked;
            BoundaryConditionType dsBcType;
            if (useLumpedStorage)
                dsBcType = BoundaryConditionType.FixedDepth;
            else
                switch (cmbDsBcType.SelectedIndex)
                {
                    case 0:  dsBcType = BoundaryConditionType.NormalDepth;  break;
                    case 1:  dsBcType = BoundaryConditionType.FixedDepth;   break;
                    default: dsBcType = BoundaryConditionType.NormalDepth;  break;
                }

            // 调蓄库启用时，用 yMin 计算初始下游水深，否则直接用界面值
            double dsInitDepth = useLumpedStorage
                ? Math.Max((double)numLsYMin.Value - dsBedLevel, 0.1)
                : dsDepth;

            // 计算河床纵坡（由上下游床底高程差 / 河道长度）
            double bedSlope = length > 0 ? (usBedLevel - dsBedLevel) / length : 1e-4;

            // 构造上下游边界对象（桩号分别为 0 和 length）
            var usBoundary = new Boundary(usBcType, 0, usBedLevel, null, null, usHydrograph);
            var dsBoundary = new Boundary(dsBcType, length, dsBedLevel, dsInitDepth);

            // 构造河道（使用渐变流方程初始化水面线）
            var channel = new Channel(usBoundary, dsBoundary, initialFlow, roughness, width,
                                      InitializationMethod.GVFEquation);

            // 不规则断面：将加载的 XS 数据（以 usBedLevel/dsBedLevel 为床底）注入河道
            if (cmbXsType.SelectedIndex == 1 && _xsX != null && _xsZ != null)
            {
                double zMinOrig = _xsZ.Min();
                // 将 Z 数组整体平移，使最低点对齐上/下游床底高程
                double[] zUs = System.Array.ConvertAll(_xsZ, z => z - zMinOrig + usBedLevel);
                double[] zDs = System.Array.ConvertAll(_xsZ, z => z - zMinOrig + dsBedLevel);
                var usXs = new IrregularSection(_xsX, zUs, roughness, bedSlope);
                var dsXs = new IrregularSection(_xsX, zDs, roughness, bedSlope);
                channel.SetCrossSection(new[] { 0.0, length }, new CrossSection[] { usXs, dsXs });
            }

            // 集总调蓄库：创建 LumpedStorage 并挂接到下游 FixedDepth 边界
            if (useLumpedStorage)
            {
                double lsYMin     = (double)numLsYMin.Value;
                double lsYMax     = (double)numLsYMax.Value;
                double lsSurfArea = (double)numLsSurfaceArea.Value;
                var ls = new LumpedStorage(lsYMin, lsYMax, lsSurfArea);
                if (cmbLsRcType.SelectedIndex > 0)   // 幂律水位流量关系
                {
                    var rc = new RatingCurve();
                    rc.Set(RatingCurveType.Power,
                           (double)numLsRcA.Value,
                           (double)numLsRcB.Value,
                           stageShift: (double)numLsRcShift.Value);
                    ls.RatingCurve = rc;
                }
                dsBoundary.SetLumpedStorage(ls);
            }

            // 读取求解器参数
            double timeStep    = (double)numTimeStep.Value;
            double spatialStep = (double)numSpatialStep.Value;
            double simTime     = (double)numSimTime.Value * 3600;   // 小时转秒

            // 根据所选格式构造求解器
            string solverType = cmbSolverMethod.SelectedItem?.ToString() ?? "Preissmann";
            Solver solver;
            if (solverType == "Lax-Friedrichs")
            {
                // Lax-Friedrichs 显式格式（受 CFL 条件约束）
                solver = new LaxSolver(channel, timeStep, spatialStep, simTime);
            }
            else
            {
                // Preissmann 隐式格式（θ 决定数值耗散与精度的平衡）
                double theta = (double)numTheta.Value;
                solver = new PreissmannSolver(channel, theta, timeStep, spatialStep, simTime);
            }
            return solver;
        }

        /// <summary>
        /// 构造三角形洪水过程线（升涨段 + 退水段 + 基流段）。
        /// <para>
        /// 过程线形状：
        /// - [0, riseTime]：从基流 (baseFlow = 10% peakFlow) 线性上升至 peakFlow；
        /// - [riseTime, riseTime + fallTime]：从 peakFlow 线性下降至 baseFlow；
        ///   fallTime = min(2·riseTime, totalTime - riseTime)（确保不超出总时长）；
        /// - [riseTime + fallTime, totalTime]：维持基流 baseFlow。
        /// </para>
        /// </summary>
        /// <param name="peakFlow">峰值流量（m³/s）。</param>
        /// <param name="riseTime">起涨历时（秒）。</param>
        /// <param name="totalTime">总模拟时长（秒）。</param>
        /// <returns>三角形洪水过程线 <see cref="Hydrograph"/> 实例。</returns>
        private static Hydrograph BuildTriangularHydrograph(double peakFlow, double riseTime, double totalTime)
        {
            double baseFlow = peakFlow * 0.1;  // 基流 = 峰值的 10%
            double fallTime = Math.Min(riseTime * 2, totalTime - riseTime);  // 退水时间不超出总时长
            return new Hydrograph(t =>
            {
                if (t <= riseTime)
                    // 升涨段：线性插值
                    return baseFlow + (peakFlow - baseFlow) * t / riseTime;
                if (t <= riseTime + fallTime)
                    // 退水段：线性下降
                    return peakFlow - (peakFlow - baseFlow) * (t - riseTime) / fallTime;
                return baseFlow;  // 基流段
            });
        }

        /// <summary>
        /// 仿真完成后更新"结果"选项卡的图表和统计信息。
        /// <para>
        /// 绘制内容：
        /// 1. 上游、中部、下游三处的流量时间过程线（Q-t 曲线）；
        /// 2. 峰值时刻的水面纵剖面（水位和床底高程 Z-x 曲线）；
        /// 3. 统计汇总表：空间步长、时间步长、节点数、时步数、
        ///    峰值入/出流量、洪峰削减率、质量不平衡率。
        /// </para>
        /// </summary>
        private void UpdateResults()
        {
            if (_solver == null || !_solver.Solved) return;

            int nk  = _solver.TimeLevel + 1;   // 实际时间层数
            int nn  = _solver.NumberOfNodes;   // 节点总数
            double dt = _solver.TimeStep;
            double[] distance = _solver.Channel.ChAtNode!;

            // ---- 绘制上游/中部/下游流量过程线（Q-t 曲线）----
            plotFlow.Plot.Clear();
            double[] times = new double[nk];
            for (int k = 0; k < nk; k++) times[k] = k * dt / 3600.0;  // 秒转小时

            int[] plotNodes    = { 0, nn / 2, nn - 1 };                     // 三个代表节点
            string[] nodeLabels = { "上游", "中部", "下游" };
            var colors = new[] { Color.Blue, Color.Green, Color.Red };
            for (int n = 0; n < plotNodes.Length; n++)
            {
                int ni = plotNodes[n];
                double[] qs = new double[nk];
                for (int k = 0; k < nk; k++) qs[k] = _solver.Flow![k, ni];
                var scatter = plotFlow.Plot.AddScatter(times, qs, label: nodeLabels[n]);
                scatter.Color      = colors[n];
                scatter.MarkerSize = 0;  // 不绘制散点符号，仅显示曲线
            }
            plotFlow.Plot.XLabel("时间（h）");
            plotFlow.Plot.YLabel("流量（m³/s）");
            plotFlow.Plot.Title("流量过程线");
            plotFlow.Plot.Legend();
            plotFlow.Refresh();

            // ---- 绘制峰值时刻水面纵剖面（Z-x 曲线）----
            plotProfile.Plot.Clear();

            // 查找上游峰值流量对应的时间层索引
            int peakTimeIndex = 0;
            double peakQ = 0;
            for (int k = 0; k < nk; k++)
                if (_solver.Flow![k, 0] > peakQ) { peakQ = _solver.Flow[k, 0]; peakTimeIndex = k; }

            double[] levels = new double[nn];
            double[] bed    = _solver.BedProfile!;
            for (int i = 0; i < nn; i++) levels[i] = _solver.Level![peakTimeIndex, i];

            // 将桩号由 m 转换为 km 以便显示
            var distKm = new double[distance.Length];
            for (int i = 0; i < distance.Length; i++) distKm[i] = distance[i] / 1000.0;

            var wlScatter  = plotProfile.Plot.AddScatter(distKm, levels, label: "峰值水位");
            wlScatter.Color      = Color.Blue;        wlScatter.MarkerSize = 0;
            var bedScatter = plotProfile.Plot.AddScatter(distKm, bed, label: "床底高程");
            bedScatter.Color     = Color.SaddleBrown; bedScatter.MarkerSize = 0;
            plotProfile.Plot.XLabel("距离（km）");
            plotProfile.Plot.YLabel("高程（m）");
            plotProfile.Plot.Title("峰值水面纵剖面");
            plotProfile.Plot.Legend();
            plotProfile.Refresh();

            // ---- 计算统计汇总 ----
            double peakIn = 0, peakOut = 0, sumQin = 0, massImbVol = 0;
            for (int k = 0; k < nk; k++)
            {
                double qIn  = _solver.Flow![k, 0];
                double qOut = _solver.Flow![k, nn - 1];
                peakIn     = Math.Max(peakIn, qIn);
                peakOut    = Math.Max(peakOut, qOut);
                sumQin     += qIn;
                massImbVol += (qIn - qOut) * dt;  // 累计体积不平衡（m³）
            }
            double atten = peakIn > 0 ? (peakIn - peakOut) / peakIn * 100 : 0;
            // 质量不平衡百分比 = 体积不平衡 / 总入流体积 × 100%
            double totalInflowVol = sumQin * dt;
            double massImbPct = totalInflowVol > 0 ? massImbVol / totalInflowVol * 100 : 0;

            // 填充统计汇总表格
            gridSummary.Rows.Clear();
            gridSummary.Rows.Add("空间步长（m）",    $"{_solver.SpatialStep:F1}");
            gridSummary.Rows.Add("时间步长（s）",    $"{_solver.TimeStep:F1}");
            gridSummary.Rows.Add("节点数量",          _solver.NumberOfNodes);
            gridSummary.Rows.Add("时间步数",          _solver.TimeLevel + 1);
            gridSummary.Rows.Add("峰值入流（m³/s）", $"{peakIn:F2}");
            gridSummary.Rows.Add("峰值出流（m³/s）", $"{peakOut:F2}");
            gridSummary.Rows.Add("洪峰削减率（%）",  $"{atten:F2}");
            gridSummary.Rows.Add("质量不平衡（%）",  $"{massImbPct:F4}");
        }

        /// <summary>
        /// 在日志文本框追加一行消息。
        /// 若在非 UI 线程调用（如后台任务），则通过 Invoke 切回 UI 线程再追加，
        /// 以遵守 WinForms 的线程安全规则。
        /// </summary>
        /// <param name="message">要追加的日志消息。</param>
        private void Log(string message)
        {
            if (txtLog.InvokeRequired)
                txtLog.Invoke(() => txtLog.AppendText(message + Environment.NewLine));
            else
                txtLog.AppendText(message + Environment.NewLine);
        }
    }
}
