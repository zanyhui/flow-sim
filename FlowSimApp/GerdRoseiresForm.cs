using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlowSim.Models;
using ScottPlot;
using ScottPlot.WinForms;

namespace FlowSim
{
    /// <summary>
    /// GERD-Roseires 水库联合调度 WinForms 窗体。
    /// <para>
    /// 翻译自 Python <c>cases/gerd_roseires/model.py</c> 及相关模块：
    /// <list type="bullet">
    ///   <item><c>gerd_discharge.py</c> → <see cref="GerdHydrograph"/></item>
    ///   <item><c>roseires_rating_curve.py</c> → <see cref="RoseiresRatingCurve"/></item>
    ///   <item><c>settings.py</c> → 窗体默认参数</item>
    ///   <item><c>model.py</c> → <see cref="btnRun_Click"/></item>
    /// </list>
    /// </para>
    /// </summary>
    public partial class GerdRoseiresForm : Form
    {
        // ── 上次仿真结果（用于图表更新）──
        private Solver? _solver;
        private ScottPlot.Plottable.Crosshair? _chQ, _chZ;

        // ── 构造函数 ──
        public GerdRoseiresForm()
        {
            InitializeComponent();
            InitCrosshairs();
            SetDefaultPaths();
        }

        // ====================================================================
        // 初始化辅助
        // ====================================================================

        private void InitCrosshairs()
        {
            _chQ = plotFlow.Plot.AddCrosshair(0, 0);
            _chQ.IsVisible = false;
            _chZ = plotProfile.Plot.AddCrosshair(0, 0);
            _chZ.IsVisible = false;

            plotFlow.MouseMove += (s, e) =>
            {
                var coord = plotFlow.GetMouseCoordinates();
                _chQ.X = coord.x; _chQ.Y = coord.y;
                _chQ.IsVisible = true;
                plotFlow.Refresh();
            };
            plotProfile.MouseMove += (s, e) =>
            {
                var coord = plotProfile.GetMouseCoordinates();
                _chZ.X = coord.x; _chZ.Y = coord.y;
                _chZ.IsVisible = true;
                plotProfile.Refresh();
            };
        }

        /// <summary>根据代码位置推算数据目录，填入默认路径。</summary>
        private void SetDefaultPaths()
        {
            // 计算相对于可执行文件的默认路径
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // 向上查找包含 cases\ 的目录（开发模式 vs 发布模式）
            string dataDir = FindDataDir(baseDir);

            SetPath(txtXsPath,        dataDir, "composite_trapezoids.csv");
            SetPath(txtInflowPath,    dataDir, "inflow_hydrograph.csv");
            SetPath(txtVolCurvePath,  dataDir, "gerd_vol_curve.csv");
            SetPath(txtSpillwayPath,  dataDir, "roseires_spillway_releases.csv");
            SetPath(txtSluicePath,     dataDir, "roseires_deep_sluice_releases.csv");
            SetPath(txtCoordsPath,    dataDir, "centerline_coords.csv");
        }

        private static string FindDataDir(string startDir)
        {
            string candidate = System.IO.Path.Combine(startDir, "cases", "gerd_roseires", "data");
            if (System.IO.Directory.Exists(candidate)) return candidate;
            // 向上最多 6 级
            string d = startDir;
            for (int i = 0; i < 6; i++)
            {
                d = System.IO.Path.GetDirectoryName(d) ?? d;
                candidate = System.IO.Path.Combine(d, "cases", "gerd_roseires", "data");
                if (System.IO.Directory.Exists(candidate)) return candidate;
            }
            return startDir;
        }

        private static void SetPath(TextBox txt, string dir, string file)
        {
            string p = System.IO.Path.Combine(dir, file);
            txt.Text = System.IO.File.Exists(p) ? p : string.Empty;
        }

        // ====================================================================
        // 浏览按钮
        // ====================================================================

        private void BrowseCsv(TextBox target, string title)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = title,
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() == DialogResult.OK) target.Text = dlg.FileName;
        }

        private void btnBrowseXs_Click(object sender, EventArgs e)       => BrowseCsv(txtXsPath,       "选择复式梯形断面 CSV");
        private void btnBrowseInflow_Click(object sender, EventArgs e)   => BrowseCsv(txtInflowPath,   "选择入库过程线 CSV");
        private void btnBrowseVolCurve_Click(object sender, EventArgs e) => BrowseCsv(txtVolCurvePath, "选择 GERD V-Z 曲线 CSV");
        private void btnBrowseSpillway_Click(object sender, EventArgs e) => BrowseCsv(txtSpillwayPath, "选择 Roseires 溢洪道流量 CSV");
        private void btnBrowseSluice_Click(object sender, EventArgs e)   => BrowseCsv(txtSluicePath,    "选择 Roseires 深孔泄槽流量 CSV");
        private void btnBrowseCoords_Click(object sender, EventArgs e)   => BrowseCsv(txtCoordsPath,   "选择中心线坐标 CSV（可选）");

        // 切换求解算法时显示/隐藏仅 Preissmann 专用的参数（θ 和迭代容差）
        private void cmbSolverMethod_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool isPreissmann = cmbSolverMethod.SelectedIndex == 0;
            numTheta.Enabled     = isPreissmann;
            numTolerance.Enabled = isPreissmann;
        }

        // ====================================================================
        // 运行仿真
        // ====================================================================

        private void btnRun_Click(object sender, EventArgs e)
        {
            btnRun.Enabled = false;
            txtLog.Clear();
            Log("正在构建 GERD-Roseires 仿真...");

            try
            {
                // 读取 UI 参数
                double initialGerdLevel     = (double)numGerdLevel.Value;
                double initialRoseiresLevel = (double)numRoseiresLevel.Value;
                double theta          = (double)numTheta.Value;
                double timeStep       = (double)numTimeStep.Value;
                double spatialStep    = (double)numSpatialStep.Value;
                double simTimeSec     = (double)numSimTime.Value * 3600.0;
                double tolerance      = (double)numTolerance.Value;
                int    jamSpillways   = (int)numJamSpillways.Value;
                int    jamSluices     = (int)numJamSluices.Value;
                bool   withGerd       = chkWithGerd.Checked;
                bool   useLax         = cmbSolverMethod.SelectedIndex == 1;

                // 验证文件路径
                string xsPath       = txtXsPath.Text;
                string inflowPath   = txtInflowPath.Text;
                string volPath      = txtVolCurvePath.Text;
                string spillwayPath = txtSpillwayPath.Text;
                string sluicePath    = txtSluicePath.Text;
                string coordsPath   = txtCoordsPath.Text;

                foreach (var (path, name) in new[]
                {
                    (xsPath,       "复式梯形断面 CSV"),
                    (inflowPath,   "入库过程线 CSV"),
                    (volPath,      "GERD V-Z 曲线 CSV"),
                    (spillwayPath, "Roseires 溢洪道流量 CSV"),
                    (sluicePath,    "Roseires 深孔泄槽流量 CSV"),
                })
                {
                    if (string.IsNullOrWhiteSpace(path))
                        throw new InvalidOperationException($"请选择「{name}」文件路径。");
                    if (!System.IO.File.Exists(path))
                        throw new System.IO.FileNotFoundException($"文件不存在：{path}");
                }

                // 在后台线程运行仿真
                Task.Run(() =>
                {
                    try
                    {
                        var solver = RunSimulation(
                            xsPath, inflowPath, volPath, spillwayPath, sluicePath,
                            string.IsNullOrWhiteSpace(coordsPath) ? null : coordsPath,
                            initialGerdLevel, initialRoseiresLevel,
                            theta, timeStep, spatialStep, simTimeSec, tolerance,
                            jamSpillways, jamSluices, withGerd, useLax);

                        Invoke(() =>
                        {
                            _solver = solver;
                            Log($"仿真成功完成，模拟时长 {solver.TotalSimDuration / 3600.0:F1} h。");
                            UpdateCharts(solver);
                            btnRun.Enabled = true;
                        });
                    }
                    catch (Exception ex)
                    {
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
                Log($"初始化错误：{ex.Message}");
                btnRun.Enabled = true;
            }
        }

        // ====================================================================
        // 核心仿真逻辑（翻译自 model.py）
        // ====================================================================

        private Solver RunSimulation(
            string xsPath, string inflowPath, string volCurvePath,
            string spillwayPath, string sluicePath, string? coordsPath,
            double initialGerdLevel, double initialRoseiresLevel,
            double theta, double timeStep, double spatialStep, double simTime,
            double tolerance, int jammedSpillways, int jammedSluices, bool withGerd,
            bool useLax = false)
        {
            Log("正在加载入库过程线...");
            var inflowHyd = LoadHydrographFromCsv(inflowPath);

            int duration = (int)(simTime > 0 ? simTime : GetHydrographDuration(inflowPath));

            Log($"正在构建 GERD 出库过程线（初始库水位 {initialGerdLevel} m）...");
            var gerdHyd = new GerdHydrograph();
            gerdHyd.Build(inflowHyd, timeStep, duration, initialGerdLevel, volCurvePath);

            double initialFlow = withGerd ? gerdHyd.GetAt(0) : inflowHyd.GetAt(0);
            var upstreamHyd    = withGerd ? (Hydrograph)gerdHyd : inflowHyd;

            Log("正在加载 Roseires 水库水位-流量曲线...");
            var roseiresRc = new RoseiresRatingCurve(
                initialRoseiresLevel, initialFlow,
                spillwayPath, sluicePath,
                jammedSpillways, jammedSluices);

            Log("正在加载断面几何数据...");
            var (chainages, sections) = LoadTrapezoidXs(xsPath);

            double roseiresCh  = chainages[chainages.Length - 1];
            double roseiresBed = sections[sections.Length - 1].ZMin;
            double upstreamCh  = chainages[0];

            var usBc = new Boundary(
                BoundaryConditionType.FlowHydrograph,
                upstreamCh, 0, null, null, upstreamHyd);

            var dsBc = new Boundary(
                BoundaryConditionType.RatingCurve,
                roseiresCh, roseiresBed,
                initialDepth: initialRoseiresLevel - roseiresBed,
                ratingCurve: roseiresRc);

            var channel = new Channel(usBc, dsBc, initialFlow);

            // 可选：设置平面坐标
            if (coordsPath != null && System.IO.File.Exists(coordsPath))
            {
                Log("正在加载中心线坐标...");
                var (coordArr, chArr) = LoadCenterlineCoords(coordsPath);
                channel.SetCoords(coordArr, chArr);
            }

            Log("正在设置断面...");
            channel.SetCrossSection(chainages, sections);

            Solver solver;
            if (useLax)
            {
                Log($"正在运行 Lax-Friedrichs 仿真（dt={timeStep} s，dx={spatialStep} m，T={duration / 3600.0:F0} h）...");
                var lax = new LaxSolver(channel, timeStep, spatialStep, duration);
                lax.StepCallback = (step, total, stepMs, totalSec) =>
                {
                    int totalStepsL  = lax.NumberOfTimeLevels - 1;
                    int logIntervalL = Math.Max(1, totalStepsL / 20);
                    if (step % logIntervalL == 0 || step == total)
                    {
                        double pct    = total > 0 ? (double)step / total * 100 : 100;
                        double simHrs = step * timeStep / 3600.0;
                        Invoke(() => Log($"  [{pct,5:F1}%] 步骤 {step}/{total}，t={simHrs:F1} h，步时 {stepMs:F1} ms"));
                    }
                };
                lax.Run(verbose: 0);
                solver = lax;
            }
            else
            {
                Log($"正在运行 Preissmann 仿真（θ={theta}，dt={timeStep} s，dx={spatialStep} m，T={duration / 3600.0:F0} h）...");
                var preissmann = new PreissmannSolver(channel, theta, timeStep, spatialStep, duration)
                {
                    Tolerance = tolerance
                };
                int totalSteps  = preissmann.NumberOfTimeLevels - 1;
                int logInterval = Math.Max(1, totalSteps / 20);
                preissmann.StepCallback = (step, total, stepMs, totalSec) =>
                {
                    if (step % logInterval == 0 || step == total)
                    {
                        double pct    = total > 0 ? (double)step / total * 100 : 100;
                        double simHrs = step * timeStep / 3600.0;
                        Invoke(() => Log($"  [{pct,5:F1}%] 步骤 {step}/{total}，t={simHrs:F1} h，步时 {stepMs:F1} ms"));
                    }
                };
                preissmann.Run(verbose: 0);
                solver = preissmann;
            }

            return solver;
        }

        // ====================================================================
        // 图表更新
        // ====================================================================

        private void UpdateCharts(Solver solver)
        {
            // ── Q-t 图（上游/下游流量历时曲线）──
            plotFlow.Plot.Clear();
            int n = solver.NumberOfTimeLevels;
            double[] times = new double[n];
            double[] qUs   = new double[n];
            double[] qDs   = new double[n];

            for (int t = 0; t < n; t++)
            {
                times[t] = t * solver.TimeStep / 3600.0;  // 转换为小时
                qUs[t]   = solver.Flow[t, 0];
                qDs[t]   = solver.Flow[t, solver.NumberOfNodes - 1];
            }

            var scUs = plotFlow.Plot.AddScatter(times, qUs, label: "上游流量");
            scUs.Color     = Color.DodgerBlue;
            scUs.LineWidth = 2;
            scUs.MarkerSize = 0;

            var scDs = plotFlow.Plot.AddScatter(times, qDs, label: "Roseires 出流");
            scDs.Color     = Color.OrangeRed;
            scDs.LineWidth = 2;
            scDs.MarkerSize = 0;

            plotFlow.Plot.XLabel("时间（h）");
            plotFlow.Plot.YLabel("流量（m³/s）");
            plotFlow.Plot.Title("上下游流量历时曲线");
            plotFlow.Plot.Legend();
            plotFlow.Plot.AxisAuto();
            plotFlow.Refresh();

            // ── 纵剖面水面线（最终时刻）──
            plotProfile.Plot.Clear();
            int lastT = n - 1;
            int nNodes = solver.NumberOfNodes;
            double[] xs   = new double[nNodes];
            double[] wsl  = new double[nNodes];   // 水面高程 = 床底 + 水深

            for (int i = 0; i < nNodes; i++)
            {
                xs[i]  = solver.Channel.ChAtNode![i] / 1000.0;  // km
                wsl[i] = solver.Depth[lastT, i] + solver.Channel.BedLevelAt(i);
            }

            var scBed = plotProfile.Plot.AddScatter(
                xs,
                System.Linq.Enumerable.Range(0, nNodes).Select(i => solver.Channel.BedLevelAt(i)).ToArray(),
                label: "河床高程");
            scBed.Color     = Color.SaddleBrown;
            scBed.LineWidth = 1.5f;
            scBed.MarkerSize = 0;

            var scWsl = plotProfile.Plot.AddScatter(xs, wsl, label: "水面线（末时）");
            scWsl.Color     = Color.DodgerBlue;
            scWsl.LineWidth = 2;
            scWsl.MarkerSize = 0;

            plotProfile.Plot.XLabel("桩号（km）");
            plotProfile.Plot.YLabel("高程（m）");
            plotProfile.Plot.Title("纵向水面线（末时刻）");
            plotProfile.Plot.Legend();
            plotProfile.Plot.AxisAuto();
            plotProfile.Refresh();
        }

        // ====================================================================
        // 数据加载辅助方法（翻译自 custom_functions.py）
        // ====================================================================

        /// <summary>
        /// 加载入库过程线（格式：首行=列名，第二行=单位注释，其余行=时间(h),流量(m³/s)）。
        /// </summary>
        private static Hydrograph LoadHydrographFromCsv(string path)
        {
            var lines = System.IO.File.ReadAllLines(path);
            var table = new System.Collections.Generic.List<(double t, double q)>();

            bool dataStarted = false;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 2) continue;
                if (!double.TryParse(parts[0].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double t)) continue;
                if (!double.TryParse(parts[1].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double q)) continue;
                table.Add((t * 3600, q));  // 小时转秒
                dataStarted = true;
            }

            if (!dataStarted || table.Count < 2)
                throw new InvalidOperationException("入库过程线数据不足（至少需要 2 行数据）。");

            var arr = new double[table.Count, 2];
            for (int i = 0; i < table.Count; i++)
            {
                arr[i, 0] = table[i].t;
                arr[i, 1] = table[i].q;
            }
            return new Hydrograph(table: arr);
        }

        /// <summary>返回过程线数据的总时长（秒）。</summary>
        private static double GetHydrographDuration(string path)
        {
            var lines = System.IO.File.ReadAllLines(path);
            double lastTime = 0;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 1) continue;
                if (double.TryParse(parts[0].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double t))
                    lastTime = t;
            }
            return lastTime * 3600;  // 小时转秒
        }

        /// <summary>
        /// 加载复式梯形断面（composite_trapezoids.csv），翻译自 custom_functions.load_trapzoid_xs。
        /// 跳过 53.csv 断面（原 Python 代码中也跳过）。
        /// </summary>
        private static (double[] chainages, CrossSection[] sections) LoadTrapezoidXs(string path)
        {
            var lines = System.IO.File.ReadAllLines(path);
            if (lines.Length < 2) throw new InvalidOperationException("断面数据文件内容不足。");

            // 解析列头
            var headers = lines[0].Split(',');
            int ColIdx(string name)
            {
                for (int j = 0; j < headers.Length; j++)
                    if (string.Equals(headers[j].Trim(), name, StringComparison.OrdinalIgnoreCase)) return j;
                throw new KeyNotFoundException($"CSV 列 '{name}' 未找到。");
            }

            int iChainage  = ColIdx("chainage");
            int iFile      = ColIdx("file");
            int iZMin      = ColIdx("z_min");
            int iBMain     = ColIdx("b_main");
            int iMMain     = ColIdx("m_main");
            int iNMain     = ColIdx("n_main");
            int iZBank     = ColIdx("z_min");  // z_bank = z_min + h_bankfull
            int iHBankfull = ColIdx("h_bankfull");
            int iBFpLeft   = ColIdx("b_fp_left");
            int iBFpRight  = ColIdx("b_fp_right");
            int iMFp       = ColIdx("m_fp");
            int iNLeft     = ColIdx("n_left");
            int iNRight    = ColIdx("n_right");

            var chs = new System.Collections.Generic.List<double>();
            var xss = new System.Collections.Generic.List<CrossSection>();

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                var parts = lines[i].Split(',');
                if (parts.Length <= iNRight) continue;

                // 跳过 53.csv（与 Python 一致）
                string fileName = parts[iFile].Trim().Trim('"');
                if (fileName.Equals("53.csv", StringComparison.OrdinalIgnoreCase)) continue;

                double ch = ParseD(parts[iChainage]);
                double zMin     = ParseD(parts[iZMin]);
                double bMain    = ParseD(parts[iBMain]);
                double mMain    = ParseD(parts[iMMain]);
                double nMain    = ParseD(parts[iNMain]);
                double hBank    = ParseD(parts[iHBankfull]);
                double bFpLeft  = ParseD(parts[iBFpLeft]);
                double bFpRight = ParseD(parts[iBFpRight]);
                double mFp      = ParseD(parts[iMFp]);
                double nLeft    = ParseD(parts[iNLeft]);
                double nRight   = ParseD(parts[iNRight]);

                var xs = new TrapezoidalSection(
                    bMain, mMain, zMin, nMain,
                    zBank:    zMin + hBank,
                    bFpLeft:  bFpLeft,
                    bFpRight: bFpRight,
                    mFp:      mFp,
                    nLeft:    nLeft,
                    nRight:   nRight);

                chs.Add(ch);
                xss.Add(xs);
            }

            if (chs.Count < 2)
                throw new InvalidOperationException("有效断面数不足（至少需要 2 个）。");

            return (chs.ToArray(), xss.ToArray());
        }

        private static (double[,] coords, double[] chainages) LoadCenterlineCoords(string path)
        {
            var lines = System.IO.File.ReadAllLines(path);
            var chList = new System.Collections.Generic.List<double>();
            var xList  = new System.Collections.Generic.List<double>();
            var yList  = new System.Collections.Generic.List<double>();

            bool first = true;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split(',');
                if (parts.Length < 3) continue;
                if (first && !double.TryParse(parts[0].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out _))
                {
                    first = false;
                    continue;
                }
                first = false;
                if (!double.TryParse(parts[0].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double ch)) continue;
                if (!double.TryParse(parts[1].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double x)) continue;
                if (!double.TryParse(parts[2].Trim(),
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double y)) continue;
                chList.Add(ch); xList.Add(x); yList.Add(y);
            }

            if (chList.Count < 2) throw new InvalidOperationException("中心线坐标数据不足。");

            int n = chList.Count;
            double[,] coords = new double[n, 2];
            for (int i = 0; i < n; i++) { coords[i, 0] = xList[i]; coords[i, 1] = yList[i]; }

            return (coords, chList.ToArray());
        }

        private static double ParseD(string s)
        {
            double.TryParse(s.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v);
            return v;
        }

        // ====================================================================
        // 日志
        // ====================================================================

        private void Log(string msg)
        {
            if (InvokeRequired) { Invoke(() => Log(msg)); return; }
            txtLog.AppendText(msg + Environment.NewLine);
        }
    }
}
