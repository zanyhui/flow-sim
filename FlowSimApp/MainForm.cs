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

        /// <summary>断面_测点.csv 解析结果：断面名称 → (起点距[], 高程[]) 测点数组映射。</summary>
        private System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>? _xsMeasPts;

        /// <summary>断面_索引.csv 解析结果：(断面名称, 起点里程, 糙率n, 备注) 列表，按起点里程升序排列。</summary>
        private System.Collections.Generic.List<(string name, double chainage, double n, string remark)>? _xsIndex;

        /// <summary>图表鼠标悬停十字准线（每个 FormsPlot 各一个）。</summary>
        private ScottPlot.Plottable.Crosshair? _chFlow, _chProfile, _chLong, _chXs;

        /// <summary>
        /// 构造函数：调用 WinForms 生成的控件初始化代码，并为所有图表安装鼠标悬停十字准线。
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
            InitCrosshairs();
        }

        /// <summary>
        /// 为四个图表控件各添加一个 ScottPlot 十字准线，并绑定鼠标事件。
        /// 鼠标悬停时十字准线随光标移动，在坐标轴边缘显示当前 X/Y 数值标注；
        /// 鼠标离开后隐藏十字准线。
        /// </summary>
        private void InitCrosshairs()
        {
            _chFlow    = AttachCrosshair(plotFlow,        () => _chFlow);
            _chProfile = AttachCrosshair(plotProfile,     () => _chProfile);
            _chLong    = AttachCrosshair(plotLongProfile, () => _chLong);
            _chXs      = AttachCrosshair(plotXsShape,     () => _chXs);
        }

        /// <summary>
        /// 向指定 <see cref="FormsPlot"/> 控件添加十字准线并绑定鼠标事件。
        /// 鼠标移动时通过 <paramref name="getCh"/> 获取当前最新的十字准线实例，
        /// 支持 <see cref="ReAddCrosshair"/> 后仍能正确更新坐标。
        /// <para>
        /// <paramref name="getCh"/> 返回可空类型：若字段尚未赋值（理论上不应发生），
        /// 则事件处理器提前返回，避免 NullReferenceException。
        /// </para>
        /// </summary>
        private static ScottPlot.Plottable.Crosshair AttachCrosshair(
            FormsPlot fp,
            Func<ScottPlot.Plottable.Crosshair?> getCh)
        {
            var ch = fp.Plot.AddCrosshair(0, 0);
            ch.IsVisible                       = false;
            ch.HorizontalLine.PositionLabel    = true;
            ch.VerticalLine.PositionLabel      = true;
            ch.LineWidth                       = 1;
            ch.Color                           = Color.FromArgb(160, Color.DimGray);

            fp.MouseMove  += (s, e) =>
            {
                var cur = getCh();
                if (cur == null) return;   // 字段尚未赋值（构造期间不处理事件，此处仅作防御）
                (double x, double y) = fp.Plot.GetCoordinate((float)e.X, (float)e.Y);
                cur.X         = x;
                cur.Y         = y;
                cur.IsVisible = true;
                fp.Refresh();
            };
            fp.MouseLeave += (s, e) =>
            {
                var cur = getCh();
                if (cur == null) return;
                cur.IsVisible = false;
                fp.Refresh();
            };

            return ch;
        }

        /// <summary>
        /// 在调用 <see cref="ScottPlot.Plot.Clear()"/> 后重新向图表中添加十字准线并更新对应字段。
        /// </summary>
        private ScottPlot.Plottable.Crosshair ReAddCrosshair(FormsPlot fp)
        {
            var ch = fp.Plot.AddCrosshair(0, 0);
            ch.IsVisible                    = false;
            ch.HorizontalLine.PositionLabel = true;
            ch.VerticalLine.PositionLabel   = true;
            ch.LineWidth                    = 1;
            ch.Color                        = Color.FromArgb(160, Color.DimGray);

            if      (fp == plotFlow)        _chFlow    = ch;
            else if (fp == plotProfile)     _chProfile = ch;
            else if (fp == plotLongProfile) _chLong    = ch;
            else if (fp == plotXsShape)     _chXs      = ch;

            return ch;
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

                // 计算进度日志频率：每 ~5% 输出一次（至少每步输出一次）
                int totalSteps = solver.NumberOfTimeLevels - 1;
                int logInterval = Math.Max(1, totalSteps / 20);

                // 进度回调：在后台线程记录计算时长，通过 Invoke 更新日志
                solver.StepCallback = (step, total, stepMs, totalSec) =>
                {
                    if (step % logInterval == 0 || step == total)
                    {
                        double pct     = total > 0 ? (double)step / total * 100.0 : 100.0;
                        double simHrs  = step * solver.TimeStep / 3600.0;
                        string msg     = $"[{pct,5:F1}%] 步骤 {step}/{total}，" +
                                         $"模拟时刻 {simHrs:F1} h，" +
                                         $"步时 {stepMs:F1} ms，累计 {totalSec:F2} s";
                        Log(msg);
                    }
                };

                // Lax-Friedrichs：注册 CFL 警告回调，将超限警告输出到日志
                if (solver is LaxSolver lax)
                    lax.CflWarningCallback = msg => Log(msg);

                Log($"正在运行仿真（{totalSteps} 步）...");
                // 在后台线程运行仿真，保持 UI 响应
                Task.Run(() =>
                {
                    try
                    {
                        _solver.Run(verbose: 0);   // verbose=0 避免 Console 输出过多
                        // 仿真完成后回到 UI 线程更新界面
                        Invoke(() =>
                        {
                            Log($"仿真成功完成，模拟时长 {_solver.TotalSimDuration / 3600.0:F1} h。");
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
        /// 加载断面_测点 CSV 文件。
        /// <para>
        /// 文件格式：首行为标题（断面名称,起点距,高程），后续每行为一个测点记录。
        /// 多个断面的测点可写在同一文件中，以断面名称区分。
        /// 解析成功后将各断面的横坐标和高程数组存入 <see cref="_xsMeasPts"/>。
        /// </para>
        /// </summary>
        private void btnLoadXsPts_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "选择断面_测点 CSV 文件（断面名称, 起点距, 高程）",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var pts = new System.Collections.Generic.Dictionary<
                    string,
                    (System.Collections.Generic.List<double> x,
                     System.Collections.Generic.List<double> z)>(StringComparer.OrdinalIgnoreCase);

                bool skipHeader = true;
                foreach (var line in System.IO.File.ReadAllLines(dlg.FileName))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    // 首行若第 2 列含非数字，视为标题行跳过
                    if (skipHeader)
                    {
                        skipHeader = false;
                        if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out _))
                            continue;
                    }
                    string name = parts[0].Trim();
                    if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double x)) continue;
                    if (!double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double z)) continue;
                    if (!pts.ContainsKey(name))
                        pts[name] = (new System.Collections.Generic.List<double>(),
                                     new System.Collections.Generic.List<double>());
                    pts[name].x.Add(x);
                    pts[name].z.Add(z);
                }

                foreach (var kvp in pts)
                    if (kvp.Value.x.Count < 3)
                        throw new InvalidOperationException(
                            $"断面「{kvp.Key}」测点不足（至少需要 3 个点，当前 {kvp.Value.x.Count} 个）。");
                if (pts.Count == 0)
                    throw new InvalidOperationException("未解析到任何断面测点数据。");

                _xsMeasPts = new System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>(
                    StringComparer.OrdinalIgnoreCase);
                int totalPts = 0;
                foreach (var kvp in pts)
                {
                    _xsMeasPts[kvp.Key] = (kvp.Value.x.ToArray(), kvp.Value.z.ToArray());
                    totalPts += kvp.Value.x.Count;
                }

                lblXsPtsFile.Text = System.IO.Path.GetFileName(dlg.FileName);
                Log($"已加载断面测点文件：{dlg.FileName}（{_xsMeasPts.Count} 个断面，共 {totalPts} 个测点）");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载断面_测点 CSV 失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>
        /// 加载断面_索引 CSV 文件。
        /// <para>
        /// 文件格式：首行为标题（断面名称,起点里程,曼宁系数n,备注），后续每行为一个断面索引记录。
        /// 备注列可省略。解析成功后将数据存入 <see cref="_xsIndex"/>，并按起点里程升序排列。
        /// </para>
        /// </summary>
        private void btnLoadXsIdx_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "选择断面_索引 CSV 文件（断面名称, 起点里程, 曼宁系数n, 备注）",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var idx = new System.Collections.Generic.List<(string name, double chainage, double n, string remark)>();
                bool skipHeader = true;
                foreach (var line in System.IO.File.ReadAllLines(dlg.FileName))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                    var parts = line.Split(',');
                    if (parts.Length < 3) continue;
                    // 首行若第 2 列含非数字，视为标题行跳过
                    if (skipHeader)
                    {
                        skipHeader = false;
                        if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out _))
                            continue;
                    }
                    string name = parts[0].Trim();
                    if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double chainage)) continue;
                    if (!double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double n)) continue;
                    string remark = parts.Length > 3 ? parts[3].Trim() : string.Empty;
                    idx.Add((name, chainage, n, remark));
                }

                if (idx.Count < 2)
                    throw new InvalidOperationException("断面索引记录不足（至少需要 2 条记录）。");

                // 按起点里程升序排列
                idx.Sort((a, b) => a.chainage.CompareTo(b.chainage));
                _xsIndex = idx;

                lblXsIdxFile.Text = System.IO.Path.GetFileName(dlg.FileName);
                Log($"已加载断面索引文件：{dlg.FileName}（{_xsIndex.Count} 条记录）");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载断面_索引 CSV 失败：{ex.Message}", "错误",
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

            // 不规则断面：由断面_测点 + 断面_索引两个 CSV 文件构建多断面模型
            if (cmbXsType.SelectedIndex == 1)
            {
                if (_xsMeasPts == null || _xsIndex == null)
                    throw new InvalidOperationException(
                        "选择不规则断面时，须先加载断面_测点 CSV 和断面_索引 CSV 文件。");

                var chainageList = new System.Collections.Generic.List<double>();
                var sectionList  = new System.Collections.Generic.List<CrossSection>();

                foreach (var (name, chainage, n, _) in _xsIndex)
                {
                    if (!_xsMeasPts.TryGetValue(name, out var pts))
                    {
                        Log($"警告：断面索引中的断面「{name}」在测点文件中未找到，已跳过。");
                        continue;
                    }
                    chainageList.Add(chainage);
                    sectionList.Add(new IrregularSection(pts.x, pts.z, n));
                }

                if (chainageList.Count < 2)
                    throw new InvalidOperationException(
                        "有效的不规则断面数量不足（至少需要 2 个）。请检查断面名称是否匹配。");

                channel.SetCrossSection(chainageList.ToArray(), sectionList.ToArray());
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
                double theta     = (double)numTheta.Value;
                double tolerance = (double)numTolerance.Value;
                int    maxIter   = (int)numMaxIter.Value;
                var ps = new PreissmannSolver(channel, theta, timeStep, spatialStep, simTime)
                {
                    Tolerance     = tolerance,
                    MaxIterations = maxIter
                };
                solver = ps;
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
            _chFlow = ReAddCrosshair(plotFlow);   // Plot.Clear() 移除了十字准线，需重新加入
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
            _chProfile = ReAddCrosshair(plotProfile);

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

            // 填充 CFL 条件查看表格（仅 Lax-Friedrichs 格式有效）
            gridCfl.Rows.Clear();
            if (_solver is LaxSolver laxSolver && laxSolver.MaxCflPerStep != null)
            {
                double maxCflAll = 0;
                for (int k = 1; k < nk; k++)
                {
                    double cfl = laxSolver.MaxCflPerStep[k];
                    maxCflAll = Math.Max(maxCflAll, cfl);
                    gridCfl.Rows.Add(k, $"{k * dt / 3600.0:F3}", $"{cfl:F4}");
                    // 超过 1.0 的行高亮显示
                    if (cfl > 1.0)
                        gridCfl.Rows[gridCfl.Rows.Count - 1].DefaultCellStyle.BackColor = Color.LightSalmon;
                }
                gridSummary.Rows.Add("最大 CFL 数",   $"{maxCflAll:F4}");
            }
            else
            {
                gridCfl.Rows.Add("—", "—", "（仅 Lax-Friedrichs 格式显示 CFL）");
            }

            // 初始化图表选项卡
            UpdateChartsTab();
        }

        /// <summary>
        /// 仿真完成后初始化"图表"选项卡：设置滑块范围、填充节点下拉框并绘制初始图表。
        /// </summary>
        private void UpdateChartsTab()
        {
            if (_solver == null || !_solver.Solved) return;

            int nk = _solver.TimeLevel + 1;
            int nn = _solver.NumberOfNodes;
            double[] chainages = _solver.Channel.ChAtNode!;

            // 设置纵断面时间滑块范围
            trkLongTime.Minimum = 0;
            trkLongTime.Maximum = nk - 1;
            trkLongTime.Value   = 0;
            trkLongTime.TickFrequency = Math.Max(1, (nk - 1) / 20);
            trkLongTime.Enabled = true;

            // 设置横断面时间滑块范围
            trkXsTime.Minimum = 0;
            trkXsTime.Maximum = nk - 1;
            trkXsTime.Value   = 0;
            trkXsTime.TickFrequency = Math.Max(1, (nk - 1) / 20);
            trkXsTime.Enabled = true;

            // 填充节点下拉框（显示桩号 km）
            cmbXsNode.Items.Clear();
            for (int i = 0; i < nn; i++)
                cmbXsNode.Items.Add($"节点 {i}（{chainages[i] / 1000.0:F1} km）");
            cmbXsNode.Enabled = true;

            // 防止 SelectedIndexChanged 在此期间触发重复绘制
            cmbXsNode.SelectedIndexChanged -= cmbXsNode_SelectedIndexChanged;
            cmbXsNode.SelectedIndex = nn / 2;   // 默认选中中间节点
            cmbXsNode.SelectedIndexChanged += cmbXsNode_SelectedIndexChanged;

            // 绘制初始图表
            UpdateLongProfile();
            UpdateXsChart();
        }

        /// <summary>
        /// 根据纵断面时间滑块当前值，重绘纵断面水位-距离图。
        /// X 轴：桩号（km）；Y 轴：绝对高程（m）；显示水面线和床底线。
        /// </summary>
        private void UpdateLongProfile()
        {
            if (_solver == null || !_solver.Solved) return;

            int k  = trkLongTime.Value;
            double tHrs = k * _solver.TimeStep / 3600.0;
            lblLongTime.Text = $"{tHrs:F1} 小时";

            int nn         = _solver.NumberOfNodes;
            double[] dist  = _solver.Channel.ChAtNode!;
            var distKm     = Array.ConvertAll(dist, d => d / 1000.0);
            double[] wl    = new double[nn];
            for (int i = 0; i < nn; i++) wl[i] = _solver.Level![k, i];

            plotLongProfile.Plot.Clear();
            _chLong = ReAddCrosshair(plotLongProfile);   // 重新加入十字准线

            // 绘制床底纵剖面（棕色填充区域）
            var bedLine = plotLongProfile.Plot.AddScatter(distKm, _solver.BedProfile!, label: "床底");
            bedLine.Color      = Color.SaddleBrown;
            bedLine.MarkerSize = 0;
            bedLine.LineWidth  = 1.5f;

            // 绘制水面线（蓝色）
            var wlLine = plotLongProfile.Plot.AddScatter(distKm, wl, label: "水面");
            wlLine.Color      = Color.DodgerBlue;
            wlLine.MarkerSize = 0;
            wlLine.LineWidth  = 2;

            plotLongProfile.Plot.XLabel("距离（km）");
            plotLongProfile.Plot.YLabel("高程（m）");
            plotLongProfile.Plot.Title($"纵断面水位（t = {tHrs:F1} h）");
            plotLongProfile.Plot.Legend();
            plotLongProfile.Refresh();
        }

        /// <summary>
        /// 根据横断面节点下拉框和时间滑块当前值，重绘横断面形状及水位图。
        /// X 轴：断面横坐标（m）；Y 轴：高程（m）；显示地形轮廓和水位线。
        /// </summary>
        private void UpdateXsChart()
        {
            if (_solver == null || !_solver.Solved) return;

            int nodeIdx = cmbXsNode.SelectedIndex;
            if (nodeIdx < 0) return;

            int k       = trkXsTime.Value;
            double tHrs = k * _solver.TimeStep / 3600.0;
            lblXsTime.Text = $"{tHrs:F1} 小时";

            var xs          = _solver.Channel.XsAtNode![nodeIdx];
            double wl       = _solver.Level![k, nodeIdx];
            double depth    = wl - xs.ZMin;
            double maxDepth = Math.Max(depth + 1.0, 2.0);   // 水面以上留 1 m 余量

            var (xPts, zPts) = xs.GetDisplayShape(maxDepth);

            plotXsShape.Plot.Clear();
            _chXs = ReAddCrosshair(plotXsShape);   // 重新加入十字准线

            // 绘制地形轮廓多边形（棕色填充）
            try
            {
                var terrain = plotXsShape.Plot.AddPolygon(xPts, zPts);
                terrain.FillColor  = Color.FromArgb(200, Color.SandyBrown);
                terrain.LineColor  = Color.SaddleBrown;
                terrain.LineWidth  = 1.5f;
            }
            catch (Exception ex)
            {
                // AddPolygon 失败时（如 ScottPlot API 不支持），回退为折线绘制
                Log($"地形多边形绘制失败（{ex.GetType().Name}），改用折线绘制。");
                var terrainLine = plotXsShape.Plot.AddScatter(xPts, zPts);
                terrainLine.Color      = Color.SaddleBrown;
                terrainLine.MarkerSize = 0;
                terrainLine.LineWidth  = 2;
            }

            // 绘制水位线（蓝色）
            if (depth > 0)
            {
                double xLeft  = xPts[0];
                double xRight = xPts[xPts.Length - 1];
                var wlLine = plotXsShape.Plot.AddScatter(
                    new[] { xLeft, xRight },
                    new[] { wl, wl },
                    label: $"水位 {wl:F2} m");
                wlLine.Color      = Color.DodgerBlue;
                wlLine.MarkerSize = 0;
                wlLine.LineWidth  = 2.5f;
            }

            double chainage = _solver.Channel.ChAtNode![nodeIdx];
            plotXsShape.Plot.XLabel("断面横坐标（m）");
            plotXsShape.Plot.YLabel("高程（m）");
            plotXsShape.Plot.Title($"断面形状（桩号 {chainage / 1000.0:F1} km，t = {tHrs:F1} h，水深 {Math.Max(depth, 0):F2} m）");
            plotXsShape.Plot.Legend();
            plotXsShape.Refresh();
        }

        /// <summary>纵断面时间滑块滚动事件：重绘纵断面图。</summary>
        private void trkLongTime_Scroll(object? sender, EventArgs e) => UpdateLongProfile();

        /// <summary>横断面时间滑块滚动事件：重绘横断面图。</summary>
        private void trkXsTime_Scroll(object? sender, EventArgs e) => UpdateXsChart();

        /// <summary>横断面节点下拉框切换事件：重绘横断面图。</summary>
        private void cmbXsNode_SelectedIndexChanged(object? sender, EventArgs e) => UpdateXsChart();

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
