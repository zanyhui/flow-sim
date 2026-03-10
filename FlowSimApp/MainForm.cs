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
    ///   <item>点击"运行仿真"按钮，在后台线程中调用 Preissmann、Lax-Friedrichs 或 HLLC 求解器；</item>
    ///   <item>仿真完成后，在"结果"选项卡展示流量过程线、水面纵剖面图以及统计汇总；</item>
    ///   <item>点击"保存结果"按钮，将计算结果导出为 Excel 和文本摘要文件。</item>
    /// </list>
    /// </para>
    /// </summary>
    public partial class MainForm : Form
    {
        /// <summary>当前仿真求解器实例（仿真前为 null）；仅单河道模式使用。</summary>
        private Solver? _solver;

        /// <summary>断面_测点.csv 解析结果：断面名称 → (起点距[], 高程[]) 测点数组映射（单河道模式）。</summary>
        private System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>? _xsMeasPts;

        /// <summary>断面_索引.csv 解析结果：按起点里程升序排列。含可选平面坐标 (x, y)（单河道模式）。</summary>
        private System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>? _xsIndex;

        /// <summary>图表鼠标悬停十字准线（每个 FormsPlot 各一个）。</summary>
        private ScottPlot.Plottable.Crosshair? _chFlow, _chProfile, _chLong, _chXs, _chXsPreview;

        /// <summary>
        /// 构造函数：调用 WinForms 生成的控件初始化代码，并为所有图表安装鼠标悬停十字准线。
        /// </summary>
        public MainForm()
        {
            InitializeComponent();
            InitCrosshairs();
        }

        /// <summary>
        /// 打开 GERD-Roseires 水库联合调度仿真窗体。
        /// </summary>
        private void btnGerdRoseires_Click(object sender, EventArgs e)
        {
            var form = new GerdRoseiresForm();
            form.Show(this);
        }

        /// <summary>
        /// 打开支流汇流仿真专用窗体。
        /// </summary>
        private void btnConfluence_Click(object sender, EventArgs e)
        {
            var form = new ConfluenceForm();
            form.Show(this);
        }

        /// <summary>
        /// 为四个图表控件各添加一个 ScottPlot 十字准线，并绑定鼠标事件。
        /// 鼠标悬停时十字准线随光标移动，在坐标轴边缘显示当前 X/Y 数值标注；
        /// 鼠标离开后隐藏十字准线。
        /// </summary>
        private void InitCrosshairs()
        {
            _chFlow      = AttachCrosshair(plotFlow,        () => _chFlow);
            _chProfile   = AttachCrosshair(plotProfile,     () => _chProfile);
            _chLong      = AttachCrosshair(plotLongProfile, () => _chLong);
            _chXs        = AttachCrosshair(plotXsShape,     () => _chXs);
            _chXsPreview = AttachCrosshair(plotXsPreview,   () => _chXsPreview);
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

            if      (fp == plotFlow)        _chFlow      = ch;
            else if (fp == plotProfile)     _chProfile   = ch;
            else if (fp == plotLongProfile) _chLong      = ch;
            else if (fp == plotXsShape)     _chXs        = ch;
            else if (fp == plotXsPreview)   _chXsPreview = ch;

            return ch;
        }

        /// <summary>
        /// 根据当前断面类型模式与已加载数据更新"运行仿真"按钮的可用状态。
        /// <list type="bullet">
        ///   <item>梯形断面：始终可用。</item>
        ///   <item>不规则断面：须同时加载测点和索引 CSV。</item>
        /// </list>
        /// </summary>
        private void UpdateRunButton()
        {
            int xsTypeIndex = cmbXsType.SelectedIndex;
            btnRun.Enabled = xsTypeIndex switch
            {
                1 => _xsMeasPts != null && _xsIndex != null,
                _ => true   // 梯形断面：无需 CSV
            };
        }

        /// <summary>
        /// "运行仿真"按钮点击事件处理器。
        /// <para>
        /// 单河道模式：调用 <see cref="BuildSolver"/> 构造求解器，后台运行并更新结果。<br/>
        /// 支流汇流模式：调用 <see cref="BuildJunctionSolver"/> 构造三段求解器，
        ///   顺序运行支流1→支流2→干流，完成后以干流求解器更新结果界面。
        /// </para>
        /// </summary>
        private void btnRun_Click(object sender, EventArgs e)
        {
            btnRun.Enabled  = false;
            btnSave.Enabled = false;
            txtLog.Clear();
            Log("正在构建仿真...");

            try
            {
                var solver = BuildSolver();
                _solver = solver;

                int totalSteps = solver.NumberOfTimeLevels - 1;
                int logInterval = Math.Max(1, totalSteps / 20);

                solver.StepCallback = (step, total, stepMs, totalSec) =>
                {
                    if (step % logInterval == 0 || step == total)
                    {
                        double pct    = total > 0 ? (double)step / total * 100.0 : 100.0;
                        double simHrs = step * solver.TimeStep / 3600.0;
                        string msg    = $"[{pct,5:F1}%] 步骤 {step}/{total}，" +
                                        $"模拟时刻 {simHrs:F1} h，" +
                                        $"步时 {stepMs:F1} ms，累计 {totalSec:F2} s";
                        Log(msg);
                    }
                };

                if (solver is LaxSolver lax)
                    lax.CflWarningCallback = msg => Log(msg);
                else if (solver is HLLCSolver hllc)
                    hllc.CflWarningCallback = msg => Log(msg);

                Log($"正在运行仿真（{totalSteps} 步）...");
                Task.Run(() =>
                {
                    try
                    {
                        _solver.Run(verbose: 0);
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
                RefreshXsPreviewDropdown();
                UpdateRunButton();
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
        /// 文件格式：首行为标题，后续每行为一个断面索引记录。<br/>
        /// 基本格式（向后兼容）：断面名称,起点里程,曼宁系数n,备注<br/>
        /// 扩展格式（含平面坐标）：断面名称,起点里程,曼宁系数n,坐标x,坐标y,备注<br/>
        /// 判断规则：若第 4、5 列均可解析为浮点数，则视为坐标 x, y；否则第 4 列视为备注。
        /// </para>
        /// </summary>
        private void btnLoadXsIdx_Click(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = "选择断面_索引 CSV 文件（断面名称, 起点里程, 曼宁系数n[, 坐标x, 坐标y], 备注）",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var idx = new System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>();
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

                    // 自动检测扩展格式（含坐标 x, y）
                    double? xCoord = null, yCoord = null;
                    string remark;
                    if (parts.Length >= 5 &&
                        double.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double px) &&
                        double.TryParse(parts[4].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double py))
                    {
                        xCoord = px; yCoord = py;
                        remark = parts.Length > 5 ? parts[5].Trim() : string.Empty;
                    }
                    else
                    {
                        remark = parts.Length > 3 ? parts[3].Trim() : string.Empty;
                    }

                    idx.Add((name, chainage, n, xCoord, yCoord, remark));
                }

                if (idx.Count < 2)
                    throw new InvalidOperationException("断面索引记录不足（至少需要 2 条记录）。");

                // 按起点里程升序排列
                idx.Sort((a, b) => a.chainage.CompareTo(b.chainage));
                _xsIndex = idx;

                int coordCount = idx.Count(r => r.x.HasValue);
                string coordInfo = coordCount > 0 ? $"，{coordCount} 个断面含平面坐标" : "";
                lblXsIdxFile.Text = System.IO.Path.GetFileName(dlg.FileName);
                Log($"已加载断面索引文件：{dlg.FileName}（{_xsIndex.Count} 条记录{coordInfo}）");
                RefreshXsPreviewDropdown();
                UpdateRunButton();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载断面_索引 CSV 失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // CSV 解析辅助方法（供单河道和汇流模式复用）
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 解析断面_测点 CSV 文件，返回 断面名→(起点距[], 高程[]) 字典。
        /// 文件格式：首行可选标题；每行为 断面名称, 起点距, 高程。
        /// </summary>
        private System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>
            ParseXsPtsFile(string path)
        {
            var raw = new System.Collections.Generic.Dictionary<
                string,
                (System.Collections.Generic.List<double> x,
                 System.Collections.Generic.List<double> z)>(StringComparer.OrdinalIgnoreCase);

            bool skipHeader = true;
            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                var parts = line.Split(',');
                if (parts.Length < 3) continue;
                if (skipHeader)
                {
                    skipHeader = false;
                    if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out _))
                        continue;
                }
                string name = parts[0].Trim();
                if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double px)) continue;
                if (!double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double pz)) continue;
                if (!raw.ContainsKey(name))
                    raw[name] = (new System.Collections.Generic.List<double>(),
                                 new System.Collections.Generic.List<double>());
                raw[name].x.Add(px);
                raw[name].z.Add(pz);
            }

            foreach (var kvp in raw)
                if (kvp.Value.x.Count < 3)
                    throw new InvalidOperationException(
                        $"断面「{kvp.Key}」测点不足（至少需要 3 个点，当前 {kvp.Value.x.Count} 个）。");
            if (raw.Count == 0)
                throw new InvalidOperationException("未解析到任何断面测点数据。");

            var result = new System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>(
                StringComparer.OrdinalIgnoreCase);
            foreach (var kvp in raw)
                result[kvp.Key] = (kvp.Value.x.ToArray(), kvp.Value.z.ToArray());
            return result;
        }

        /// <summary>
        /// 解析断面_索引 CSV 文件，返回按桩号升序排列的索引记录列表。
        /// 支持基本格式（名称, 里程, n, 备注）和扩展格式（名称, 里程, n, x, y, 备注）。
        /// </summary>
        private System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>
            ParseXsIdxFile(string path)
        {
            var idx = new System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>();
            bool skipHeader = true;
            foreach (var line in System.IO.File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith("#")) continue;
                var parts = line.Split(',');
                if (parts.Length < 3) continue;
                if (skipHeader)
                {
                    skipHeader = false;
                    if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out _))
                        continue;
                }
                string name = parts[0].Trim();
                if (!double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double ch)) continue;
                if (!double.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double nVal)) continue;
                double? xCoord = null, yCoord = null;
                string remark;
                if (parts.Length >= 5 &&
                    double.TryParse(parts[3].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double ipx) &&
                    double.TryParse(parts[4].Trim(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double ipy))
                {
                    xCoord = ipx; yCoord = ipy;
                    remark = parts.Length > 5 ? parts[5].Trim() : string.Empty;
                }
                else
                    remark = parts.Length > 3 ? parts[3].Trim() : string.Empty;
                idx.Add((name, ch, nVal, xCoord, yCoord, remark));
            }
            if (idx.Count < 2)
                throw new InvalidOperationException("断面索引记录不足（至少需要 2 条记录）。");
            idx.Sort((a, b) => a.chainage.CompareTo(b.chainage));
            return idx;
        }

        // ══════════════════════════════════════════════════════════════
        // 断面预览和平面图
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 当断面_测点或断面_索引 CSV 任一文件加载成功后，刷新"预览断面"下拉框。
        /// 若两个文件均已加载，按断面_索引顺序填充所有断面名称并启用下拉框；
        /// 同时自动预览第一个断面。
        /// </summary>
        private void RefreshXsPreviewDropdown()
        {
            cmbXsPreview.Items.Clear();
            if (_xsMeasPts == null)
            {
                cmbXsPreview.Enabled = false;
                return;
            }

            // 若索引已加载，按索引顺序添加；否则按测点字典顺序添加
            var names = _xsIndex != null
                ? _xsIndex.ConvertAll(r => r.name)
                : new System.Collections.Generic.List<string>(_xsMeasPts.Keys);

            foreach (var n in names)
                if (_xsMeasPts.ContainsKey(n))
                    cmbXsPreview.Items.Add(n);

            if (cmbXsPreview.Items.Count > 0)
            {
                cmbXsPreview.Enabled = true;
                cmbXsPreview.SelectedIndex = 0;  // 触发 SelectedIndexChanged → 绘制预览
            }
            else
            {
                cmbXsPreview.Enabled = false;
            }

            // 刷新河道平面布置图（若坐标数据可用则绘制）
            DrawChannelLayout();
        }

        /// <summary>
        /// "预览断面"下拉框选择项变更事件：绘制所选断面的横断面形状图。
        /// X 轴为起点距（m），Y 轴为高程（m），以折线绘制地形轮廓。
        /// </summary>
        private void cmbXsPreview_SelectedIndexChanged(object sender, EventArgs e)
        {
            string? name = cmbXsPreview.SelectedItem as string;
            if (name == null || _xsMeasPts == null || !_xsMeasPts.TryGetValue(name, out var pts)) return;

            plotXsPreview.Plot.Clear();
            _chXsPreview = ReAddCrosshair(plotXsPreview);

            double[] x = pts.x;
            double[] z = pts.z;

            // 绘制地形折线
            var terrain = plotXsPreview.Plot.AddScatter(x, z, label: "地形轮廓");
            terrain.Color      = Color.SaddleBrown;
            terrain.LineWidth  = 2;
            terrain.MarkerSize = 5;
            terrain.MarkerShape = ScottPlot.MarkerShape.filledCircle;

            // 若索引已加载，查找该断面对应的曼宁 n，显示在标题中
            string titleSuffix = "";
            if (_xsIndex != null)
            {
                int foundIdx = _xsIndex.FindIndex(r => string.Equals(r.name, name, StringComparison.OrdinalIgnoreCase));
                if (foundIdx >= 0)
                {
                    var rec = _xsIndex[foundIdx];
                    titleSuffix = $"，桩号 {rec.chainage / 1000.0:F1} km，n = {rec.n:F3}";
                }
            }

            plotXsPreview.Plot.XLabel("起点距（m）");
            plotXsPreview.Plot.YLabel("高程（m）");
            plotXsPreview.Plot.Title($"断面 {name}{titleSuffix}");
            plotXsPreview.Plot.AxisAuto();
            plotXsPreview.Refresh();
        }

        /// <summary>
        /// 绘制河道平面布置图（单河道模式：梯形或不规则断面）。
        /// 绘制一条中心线 + 各断面垂向短线。
        /// </summary>
        private void DrawChannelLayout()
        {
            plotChannelLayout.Plot.Clear();

            // 单河道模式
            if (_xsIndex == null)
            {
                plotChannelLayout.Plot.Title("河道平面布置图（未加载断面索引文件）");
                plotChannelLayout.Refresh();
                return;
            }
            bool drawn = DrawSingleChannelOnLayout("中心线", Color.DodgerBlue,
                _xsIndex, _xsMeasPts, firstLabel: true);
            if (!drawn)
            {
                plotChannelLayout.Plot.Title("河道平面布置图（需要 ≥ 2 个断面含坐标 x,y）");
                plotChannelLayout.Refresh();
                return;
            }

            plotChannelLayout.Plot.XLabel("X（m）");
            plotChannelLayout.Plot.YLabel("Y（m）");
            plotChannelLayout.Plot.Title("河道平面布置图");
            plotChannelLayout.Plot.Legend();
            plotChannelLayout.Plot.AxisAuto();
            plotChannelLayout.Refresh();
        }

        /// <summary>
        /// 在平面布置图上绘制单条河道的中心线和断面线。
        /// </summary>
        /// <returns>true 如果至少绘制了中心线（>=2 个坐标点），否则 false。</returns>
        private bool DrawSingleChannelOnLayout(
            string channelLabel,
            Color  channelColor,
            System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>? xsIndex,
            System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>? xsMeasPts,
            bool firstLabel)
        {
            if (xsIndex == null) return false;

            // 筛选含平面坐标的断面
            var coordSecs = new System.Collections.Generic.List<(string name, double x, double y)>();
            foreach (var rec in xsIndex)
                if (rec.x.HasValue && rec.y.HasValue)
                    coordSecs.Add((rec.name, rec.x.Value, rec.y.Value));

            if (coordSecs.Count < 2) return false;

            int n = coordSecs.Count;
            double[] clX = new double[n];
            double[] clY = new double[n];
            for (int i = 0; i < n; i++) { clX[i] = coordSecs[i].x; clY[i] = coordSecs[i].y; }

            // 中心线
            var cl = plotChannelLayout.Plot.AddScatter(clX, clY, label: channelLabel);
            cl.Color      = channelColor;
            cl.LineWidth  = 2;
            cl.MarkerSize = 5;
            cl.MarkerShape = ScottPlot.MarkerShape.filledCircle;

            // 计算备用半宽
            double totalLen = 0;
            for (int i = 1; i < n; i++)
                totalLen += Math.Sqrt(Math.Pow(clX[i] - clX[i - 1], 2) + Math.Pow(clY[i] - clY[i - 1], 2));
            double defaultHalfWidth = totalLen / (n - 1) * 0.4;

            // 断面线 + 断面名称标注
            bool isFirstStub = firstLabel;
            for (int i = 0; i < n; i++)
            {
                double tx, ty;
                if (i == 0)
                { tx = clX[1] - clX[0]; ty = clY[1] - clY[0]; }
                else if (i == n - 1)
                { tx = clX[n - 1] - clX[n - 2]; ty = clY[n - 1] - clY[n - 2]; }
                else
                { tx = clX[i + 1] - clX[i - 1]; ty = clY[i + 1] - clY[i - 1]; }

                double tLen = Math.Sqrt(tx * tx + ty * ty);
                if (tLen < 1e-12) { tx = 1; ty = 0; } else { tx /= tLen; ty /= tLen; }

                double normX = -ty, normY = tx;

                double halfWidth = defaultHalfWidth;
                if (xsMeasPts != null && xsMeasPts.TryGetValue(coordSecs[i].name, out var pts))
                {
                    double measWidth = pts.x.Length > 1 ? pts.x.Max() - pts.x.Min() : 0;
                    if (measWidth > 0) halfWidth = measWidth * 0.5;
                }

                double[] xsXArr = { clX[i] + halfWidth * normX, clX[i] - halfWidth * normX };
                double[] xsYArr = { clY[i] + halfWidth * normY, clY[i] - halfWidth * normY };

                string? lineLabel = isFirstStub ? $"{channelLabel}断面" : null;
                isFirstStub = false;
                var stub = plotChannelLayout.Plot.AddScatter(xsXArr, xsYArr, label: lineLabel);
                stub.Color     = channelColor;
                stub.LineWidth = 1.5f;
                stub.MarkerSize = 0;
                stub.LineStyle = ScottPlot.LineStyle.Solid;

                var txt = plotChannelLayout.Plot.AddText(
                    coordSecs[i].name,
                    clX[i] + halfWidth * normX * 1.15,
                    clY[i] + halfWidth * normY * 1.15);
                txt.FontSize = 7;
                txt.Color    = channelColor;
            }
            return true;
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
        /// 下游边界类型切换事件：更新水深标签文本、"自动估算"按钮可见性和常驻说明文字。
        /// <para>
        /// 正常水深（NormalDepth）：标签改为"初始水深（m）（均匀流）"，"自动估算"可用，
        ///   说明文字提示"正常水深≠坡度，坡度已由床底高程自动推算"；
        /// 固定水深（FixedDepth）：标签改为"固定水深（m）"，"自动估算"不可见，
        ///   说明文字提示水深全程固定。
        /// </para>
        /// </summary>
        private void cmbDsBcType_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool isNormal = cmbDsBcType.SelectedIndex == 0;
            lblDsDepthLabel.Text = isNormal
                ? "初始水深（m）（均匀流）："
                : "固定水深（m）：";
            btnSuggestNormalDepth.Visible = isNormal;
            lblDsBcInfo.Text = isNormal
                ? "ℹ 正常水深（m）≠ 坡度。坡度由上/下游床底高程自动推算；正常水深是在该坡度下满足曼宁公式的均匀流水深，仿真中随流量动态变化。\n⚠ 请勿将初始水深设为极小值（如 0.01 m）；请先点击「📐 自动估算」填入合理值，否则仿真将因流速过高而失败。"
                : "ℹ 固定水深：出口水深在整个仿真中保持为所填数值（m），不随流量变化。";
        }

        /// <summary>
        /// "自动估算正常水深"按钮点击事件。
        /// 根据当前界面的初始流量、河道宽度、糙率和河床坡度，
        /// 使用曼宁公式（Brent 求根法）计算下游正常水深，并填入 <see cref="numDsDepth"/>。
        /// <para>
        /// 正常水深（Normal Depth）是使摩阻坡度等于河床坡度的均匀流水深，
        /// 即满足 Q = (1/n)·A·R^(2/3)·S0^(1/2) 时的水深。
        /// 它是下游 NormalDepth 边界的最合适初始水深——既保持初始均匀流状态，
        /// 又允许仿真过程中水深随流量变化而动态调整。
        /// </para>
        /// </summary>
        private void btnSuggestNormalDepth_Click(object sender, EventArgs e)
        {
            try
            {
                double q      = (double)numInitialFlow.Value;
                double width  = (double)numWidth.Value;
                double n      = (double)numRoughness.Value;
                double usBed  = (double)numUsBedLevel.Value;
                double dsBed  = (double)numDsBedLevel.Value;
                double length = (double)numLength.Value;

                if (length <= 0)
                {
                    MessageBox.Show("河道长度必须大于 0。", "提示",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                double slope = (usBed - dsBed) / length;
                if (slope <= 0)
                {
                    MessageBox.Show(
                        "河床纵坡必须大于 0 才能计算正常水深。\n" +
                        "（需要上游床底高程 > 下游床底高程）",
                        "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // 用矩形断面（bMain=width, mMain=0 即垂直边坡）近似计算正常水深
                var xs = new FlowSim.Models.TrapezoidalSection(width, 0, dsBed, n, slope);
                double hn = xs.NormalDepth(q);
                hn = Math.Max(hn, 0.01);   // 与 numDsDepth 控件最小值 0.01 m 保持一致，防止计算结果为 0

                numDsDepth.Value = (decimal)Math.Round(hn, 2);
                Log($"正常水深估算：Q={q:F0} m³/s，B={width:F0} m，n={n}，S₀={slope:G3} → h_n = {hn:F2} m");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"计算正常水深失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }


        /// <summary>
        /// 根据界面控件当前值构造一维河道和求解器。
        /// <para>
        /// 构造流程：
        /// 1. 读取河道几何参数（长度、宽度、糙率、床底高程等）；
        ///    若选择不规则断面，则自动从索引/测点数据中推导长度和床底高程；
        /// 2. 根据上游边界类型（流量过程线 or 正常水深）构造上游边界；
        ///    若选择流量过程线，则调用 <see cref="BuildTriangularHydrograph"/> 构造三角形洪水过程；
        /// 3. 根据下游边界类型（正常水深 or 固定水深）构造下游边界；
        /// 4. 构造 <see cref="Channel"/> 对象（使用 GVFEquation 初始化方式）；
        /// 5. 若选择不规则断面，用 CSV 断面建立多断面模型；若索引含 x,y 坐标则调用 SetCoords；
        /// 6. 根据用户选择的格式（Preissmann、Lax-Friedrichs 或 HLLC）构造求解器。
        /// </para>
        /// </summary>
        /// <returns>初始化完成的求解器实例（<see cref="PreissmannSolver"/>、<see cref="LaxSolver"/> 或 <see cref="HLLCSolver"/>）。</returns>
        private Solver BuildSolver()
        {
            bool isIrregular = cmbXsType.SelectedIndex == 1;

            // 读取公共参数（不规则模式时某些参数从数据中覆盖）
            double width       = (double)numWidth.Value;
            double roughness   = (double)numRoughness.Value;
            double initialFlow = (double)numInitialFlow.Value;
            double dsDepth     = (double)numDsDepth.Value;

            // ---- 从 UI 读取初始几何参数 ----
            double length     = (double)numLength.Value;
            double usBedLevel = (double)numUsBedLevel.Value;
            double dsBedLevel = (double)numDsBedLevel.Value;

            // ---- 不规则断面：从数据推导几何参数 ----
            if (isIrregular)
            {
                if (_xsMeasPts == null || _xsIndex == null)
                    throw new InvalidOperationException(
                        "选择不规则断面时，须先加载断面_测点 CSV 和断面_索引 CSV 文件。");

                // 河道总长度 = 索引中最末断面桩号
                length = _xsIndex[_xsIndex.Count - 1].chainage;

                // 床底高程：从首/末断面测点中取最低点
                string firstName = _xsIndex[0].name;
                string lastName  = _xsIndex[_xsIndex.Count - 1].name;
                if (_xsMeasPts.TryGetValue(firstName, out var firstPts))
                    usBedLevel = firstPts.z.Min();
                if (_xsMeasPts.TryGetValue(lastName, out var lastPts))
                    dsBedLevel = lastPts.z.Min();
            }

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

            // 构造上下游边界对象（桩号分别为 0 和 length）
            var usBoundary = new Boundary(usBcType, 0, usBedLevel, null, null, usHydrograph);
            var dsBoundary = new Boundary(dsBcType, length, dsBedLevel, dsInitDepth);

            // 构造河道（使用渐变流方程初始化水面线）
            var channel = new Channel(usBoundary, dsBoundary, initialFlow, roughness, width,
                                      InitializationMethod.GVFEquation);

            // 不规则断面：由断面_测点 + 断面_索引两个 CSV 文件构建多断面模型
            if (isIrregular)
            {
                var chainageList = new System.Collections.Generic.List<double>();
                var sectionList  = new System.Collections.Generic.List<CrossSection>();

                // 收集有效坐标点（用于弯道曲率计算）
                var coordXs = new System.Collections.Generic.List<double>();
                var coordYs = new System.Collections.Generic.List<double>();
                var coordChs = new System.Collections.Generic.List<double>();

                foreach (var rec in _xsIndex!)
                {
                    if (!_xsMeasPts!.TryGetValue(rec.name, out var pts))
                    {
                        Log($"警告：断面索引中的断面「{rec.name}」在测点文件中未找到，已跳过。");
                        continue;
                    }
                    chainageList.Add(rec.chainage);
                    sectionList.Add(new IrregularSection(pts.x, pts.z, rec.n));

                    if (rec.x.HasValue && rec.y.HasValue)
                    {
                        coordXs.Add(rec.x.Value);
                        coordYs.Add(rec.y.Value);
                        coordChs.Add(rec.chainage);
                    }
                }

                if (chainageList.Count < 2)
                    throw new InvalidOperationException(
                        "有效的不规则断面数量不足（至少需要 2 个）。请检查断面名称是否匹配。");

                channel.SetCrossSection(chainageList.ToArray(), sectionList.ToArray());

                // 若索引含平面坐标，传入 Channel 以计算弯道曲率
                if (coordChs.Count >= 2)
                {
                    int nc = coordChs.Count;
                    double[,] coords = new double[nc, 2];
                    for (int coordIdx = 0; coordIdx < nc; coordIdx++) { coords[coordIdx, 0] = coordXs[coordIdx]; coords[coordIdx, 1] = coordYs[coordIdx]; }
                    channel.SetCoords(coords, coordChs.ToArray());
                    Log($"已设置 {nc} 个断面的平面坐标，将计算弯道曲率。");
                }
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
            else if (solverType == "HLLC")
            {
                // HLLC Riemann 显式格式（受 CFL 条件约束，比 Lax 精度更高）
                solver = new HLLCSolver(channel, timeStep, spatialStep, simTime);
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

            // 验证初始条件：若初始流速超出物理合理范围，在开始仿真前给出明确提示
            ValidateInitialVelocity(channel, dsInitDepth, isIrregular ? double.NaN : (usBedLevel - dsBedLevel) / length);

            return solver;
        }

        /// <summary>
        /// 验证河道初始条件中的最大流速是否在物理合理范围内。
        /// <para>
        /// 初始流速过高（通常由用户将正常水深设置为远小于均匀流深的值引起）会导致 CFL 条件
        /// 违反（Lax 格式）或牛顿迭代不收敛（Preissmann 格式），仿真无法进行。
        /// 若最大初始流速超过 20 m/s，抛出 <see cref="InvalidOperationException"/>，并给出建议值。
        /// </para>
        /// </summary>
        /// <param name="channel">已完成 InitializeConditions 的河道。</param>
        /// <param name="dsInitDepth">用户设置的下游初始水深（m），用于错误提示。</param>
        /// <param name="bedSlope">河床纵坡 S₀（无量纲）；若为 NaN（不规则断面）则只做流速检查，不给出建议水深。</param>
        private static void ValidateInitialVelocity(Channel channel, double dsInitDepth, double bedSlope)
        {
            const double MaxReasonableVelocity = 20.0;  // m/s — 超过此值视为初始条件不合理

            var ic = channel.InitialConditions;
            if (ic == null) return;

            int nNodes = ic.GetLength(0);
            double maxV = 0.0;
            int    maxNode = 0;
            for (int i = 0; i < nNodes; i++)
            {
                double h  = ic[i, 0];
                double Q  = Math.Abs(ic[i, 1]);
                double hw = channel.BedLevelAt(i) + h;
                double A  = channel.AreaAt(i, hw);
                if (A <= 0) continue;
                double v = Q / A;
                if (v > maxV) { maxV = v; maxNode = i; }
            }

            if (maxV > MaxReasonableVelocity)
            {
                // 构建建议信息——若坡度已知（梯形断面），估算正常水深
                string suggestion = "";
                if (!double.IsNaN(bedSlope) && bedSlope > 0 && channel.Width.HasValue && channel.Roughness.HasValue)
                {
                    try
                    {
                        var xs = new FlowSim.Models.TrapezoidalSection(
                            channel.Width.Value, 0,
                            channel.BedLevelAt(nNodes - 1),
                            channel.Roughness.Value,
                            bedSlope);
                        double hn = xs.NormalDepth(channel.InitialFlowRate);
                        suggestion = $"\n建议正常水深 ≈ {hn:F2} m。\n" +
                                     "请使用「📐 自动估算」按钮自动填入正确初始水深，再重新运行。";
                    }
                    catch { /* 无法计算则不附建议 */ }
                }

                throw new InvalidOperationException(
                    $"初始水深 {dsInitDepth:F2} m 与流量 {channel.InitialFlowRate:F0} m³/s 严重不匹配：\n" +
                    $"  节点 {maxNode} 处初始流速 V ≈ {maxV:F0} m/s，远超物理范围。\n" +
                    $"  这将导致 CFL 条件违反（Lax/HLLC）或牛顿迭代不收敛（Preissmann）。{suggestion}");
            }
        }

        /// <summary>
        /// 根据峰值流量和起涨时间构造三角形洪水过程线。
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

            // ---- 填充断面选择下拉框（暂停事件以避免重入）----
            cmbFlowNode.SelectedIndexChanged -= cmbFlowNode_SelectedIndexChanged;
            cmbFlowNode.Items.Clear();
            cmbFlowNode.Items.Add("全部代表节点");
            for (int i = 0; i < nn; i++)
                cmbFlowNode.Items.Add($"节点 {i}（{distance[i] / 1000.0:F1} km）");
            cmbFlowNode.Enabled      = true;
            cmbFlowNode.SelectedIndex = 0;
            cmbFlowNode.SelectedIndexChanged += cmbFlowNode_SelectedIndexChanged;

            // ---- 绘制流量过程线 ----
            DrawFlowChart();

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
            double peakIn = 0, peakOut = 0, sumQin = 0, sumQout = 0, massImbVol = 0;
            for (int k = 0; k < nk; k++)
            {
                double qIn  = _solver.Flow![k, 0];
                double qOut = _solver.Flow![k, nn - 1];
                peakIn     = Math.Max(peakIn, qIn);
                peakOut    = Math.Max(peakOut, qOut);
                sumQin     += qIn;
                sumQout    += qOut;
                massImbVol += (qIn - qOut) * dt;  // 累计体积不平衡（m³）
            }
            double atten = peakIn > 0 ? (peakIn - peakOut) / peakIn * 100 : 0;
            // 质量不平衡百分比 = 体积不平衡 / 总入流体积 × 100%
            double totalInflowVol  = sumQin * dt;
            double totalOutflowVol = sumQout * dt;
            double massImbPct = totalInflowVol > 0 ? massImbVol / totalInflowVol * 100 : 0;

            // 填充统计汇总表格
            gridSummary.Rows.Clear();
            gridSummary.Rows.Add("空间步长（m）",        $"{_solver.SpatialStep:F1}");
            gridSummary.Rows.Add("时间步长（s）",        $"{_solver.TimeStep:F1}");
            gridSummary.Rows.Add("节点数量",              _solver.NumberOfNodes);
            gridSummary.Rows.Add("时间步数",              _solver.TimeLevel + 1);
            gridSummary.Rows.Add("峰值入流（m³/s）",    $"{peakIn:F2}");
            gridSummary.Rows.Add("峰值出流（m³/s）",    $"{peakOut:F2}");
            gridSummary.Rows.Add("洪峰削减率（%）",     $"{atten:F2}");
            // 水量信息
            gridSummary.Rows.Add("总入流量（万m³）",     $"{totalInflowVol / 10000.0:F2}");
            gridSummary.Rows.Add("总出流量（万m³）",     $"{totalOutflowVol / 10000.0:F2}");
            gridSummary.Rows.Add("净蓄水量变化（万m³）", $"{massImbVol / 10000.0:F2}");
            gridSummary.Rows.Add("质量不平衡（%）",     $"{massImbPct:F4}");

            // 填充 CFL 条件查看表格（Lax-Friedrichs 和 HLLC 格式有效）
            gridCfl.Rows.Clear();
            double[]? cflPerStep = _solver is LaxSolver ls ? ls.MaxCflPerStep
                                 : _solver is HLLCSolver hs ? hs.MaxCflPerStep
                                 : null;
            if (cflPerStep != null)
            {
                double maxCflAll = 0;
                for (int k = 1; k < nk; k++)
                {
                    double cfl = cflPerStep[k];
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
                gridCfl.Rows.Add("—", "—", "（仅 Lax-Friedrichs / HLLC 格式显示 CFL）");
            }

            // 初始化图表选项卡 + 数据查看选项卡
            UpdateChartsTab();
            UpdateDataTab();
        }

        /// <summary>
        /// 根据 <see cref="cmbFlowNode"/> 当前选项绘制流量过程线图表。
        /// 选项 0（"全部代表节点"）时显示上游/中部/下游三条曲线；
        /// 其余选项对应特定节点，仅绘制该节点的流量过程线。
        /// </summary>
        private void DrawFlowChart()
        {
            if (_solver == null || !_solver.Solved) return;

            int    nk = _solver.TimeLevel + 1;
            int    nn = _solver.NumberOfNodes;
            double dt = _solver.TimeStep;

            plotFlow.Plot.Clear();
            _chFlow = ReAddCrosshair(plotFlow);

            double[] times = new double[nk];
            for (int k = 0; k < nk; k++) times[k] = k * dt / 3600.0;

            int selectedIdx = cmbFlowNode.SelectedIndex;
            if (selectedIdx <= 0)
            {
                // 全部代表节点：上游、中部、下游
                int[]    plotNodes  = { 0, nn / 2, nn - 1 };
                string[] nodeLabels = { "上游", "中部", "下游" };
                var      colors     = new[] { Color.Blue, Color.Green, Color.Red };
                for (int n = 0; n < plotNodes.Length; n++)
                {
                    int ni = plotNodes[n];
                    double[] qs = new double[nk];
                    for (int k = 0; k < nk; k++) qs[k] = _solver.Flow![k, ni];
                    var scatter = plotFlow.Plot.AddScatter(times, qs, label: nodeLabels[n]);
                    scatter.Color      = colors[n];
                    scatter.MarkerSize = 0;
                }
                plotFlow.Plot.Title("流量过程线");
            }
            else
            {
                // 单节点：selectedIdx - 1 为节点编号（Item 0 是"全部"，Item 1 开始对应节点 0）
                int ni = selectedIdx - 1;
                double[] qs = new double[nk];
                for (int k = 0; k < nk; k++) qs[k] = _solver.Flow![k, ni];
                double[] chainages = _solver.Channel.ChAtNode!;
                string label = $"节点 {ni}（{chainages[ni] / 1000.0:F1} km）";
                var scatter = plotFlow.Plot.AddScatter(times, qs, label: label);
                scatter.Color      = Color.SteelBlue;
                scatter.MarkerSize = 0;
                plotFlow.Plot.Title($"流量过程线 — {label}");
            }

            plotFlow.Plot.XLabel("时间（h）");
            plotFlow.Plot.YLabel("流量（m³/s）");
            plotFlow.Plot.Legend();
            plotFlow.Refresh();
        }

        private void cmbFlowNode_SelectedIndexChanged(object? sender, EventArgs e) => DrawFlowChart();

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
        /// 仿真完成后初始化"数据"选项卡：配置时间滑块范围并填充数据表。
        /// </summary>
        private void UpdateDataTab()
        {
            if (_solver == null || !_solver.Solved) return;

            int nk = _solver.TimeLevel + 1;

            // 设置数据表时间滑块范围
            trkDataTime.Minimum       = 0;
            trkDataTime.Maximum       = nk - 1;
            trkDataTime.Value         = 0;
            trkDataTime.TickFrequency = Math.Max(1, (nk - 1) / 20);
            trkDataTime.Enabled       = true;

            UpdateDataTable();
        }

        /// <summary>
        /// 根据数据表时间滑块当前值，刷新"数据"选项卡的节点数据表。
        /// 每行显示一个计算节点的水动力状态：桩号、流量、水位、水深、过水面积、水面宽、流速、弗劳德数。
        /// </summary>
        private void UpdateDataTable()
        {
            if (_solver == null || !_solver.Solved) return;

            int k       = trkDataTime.Value;
            double tHrs = k * _solver.TimeStep / 3600.0;
            lblDataTime.Text = $"{tHrs:F1} 小时";

            int nn             = _solver.NumberOfNodes;
            double[] chainages = _solver.Channel.ChAtNode!;

            gridData.Rows.Clear();
            for (int i = 0; i < nn; i++)
            {
                double q  = _solver.Flow![k, i];
                double wl = _solver.Level![k, i];
                double h  = _solver.Depth![k, i];
                double a  = _solver.Area  != null ? _solver.Area![k, i]      : double.NaN;
                double tw = _solver.TopWidth != null ? _solver.TopWidth![k, i] : double.NaN;
                double v  = _solver.Velocity != null ? _solver.Velocity![k, i] : double.NaN;
                double fr = _solver.FroudeNumber != null ? _solver.FroudeNumber![k, i] : double.NaN;

                gridData.Rows.Add(
                    i,
                    $"{chainages[i] / 1000.0:F3}",
                    $"{q:F3}",
                    $"{wl:F3}",
                    $"{h:F3}",
                    double.IsNaN(a)  ? "—" : $"{a:F2}",
                    double.IsNaN(tw) ? "—" : $"{tw:F2}",
                    double.IsNaN(v)  ? "—" : $"{v:F3}",
                    double.IsNaN(fr) ? "—" : $"{fr:F4}");
            }
        }

        /// <summary>数据表时间滑块滚动事件：刷新数据表。</summary>
        private void trkDataTime_Scroll(object? sender, EventArgs e) => UpdateDataTable();

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
