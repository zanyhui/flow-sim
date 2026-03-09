using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using FlowSim.Models;
using ScottPlot;

namespace FlowSim
{
    /// <summary>
    /// 支流汇流仿真专用窗体。
    /// <para>
    /// 功能概述：
    /// <list type="bullet">
    ///   <item>分别加载支流1、支流2、干流的断面测点和索引 CSV 文件（共 6 个文件）；</item>
    ///   <item>配置各支流的三角形入流过程线参数（峰值流量、起涨时间）；</item>
    ///   <item>配置下游边界条件（正常水深或固定水深）和求解器格式；</item>
    ///   <item>点击"运行仿真"后，顺序求解支流1→支流2→干流（非耦合法）；</item>
    ///   <item>仿真完成后在"结果"、"图表"、"数据"、"平面图"四个选项卡中展示干流结果。</item>
    /// </list>
    /// </para>
    /// </summary>
    public partial class ConfluenceForm : Form
    {
        // ── CSV 数据字段 ──
        private System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>? _xsMeasPts1;
        private System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>? _xsIndex1;
        private System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>? _xsMeasPts2;
        private System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>? _xsIndex2;
        private System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>? _xsMeasPts3;
        private System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>? _xsIndex3;

        /// <summary>汇流求解器（含三段子求解器）。</summary>
        private JunctionSolver? _junctionSolver;

        /// <summary>干流求解器（仿真后赋值）。</summary>
        private Solver? _solver;

        /// <summary>支流1求解器（仿真后赋值，用于填充支流1结果选项卡）。</summary>
        private Solver? _solver1;

        /// <summary>支流2求解器（仿真后赋值，用于填充支流2结果选项卡）。</summary>
        private Solver? _solver2;

        /// <summary>
        /// 最近一次 BuildJunctionSolver 调用时是否交换了支流顺序（支流1桩号 > 支流2桩号）。
        /// 用于在仿真完成后将排序后的 TributarySolvers 正确映射回 UI 中的支流1/支流2。
        /// </summary>
        private bool _lastBuildSwapTribs;

        /// <summary>图表鼠标悬停十字准线。</summary>
        private ScottPlot.Plottable.Crosshair? _chFlow, _chProfile, _chLong, _chXs;
        private ScottPlot.Plottable.Crosshair? _chJunction;
        private ScottPlot.Plottable.Crosshair? _chTrib1Flow, _chTrib1Profile;
        private ScottPlot.Plottable.Crosshair? _chTrib2Flow, _chTrib2Profile;

        public ConfluenceForm()
        {
            InitializeComponent();
            InitCrosshairs();
        }

        // ══════════════════════════════════════════════════════════════
        // 初始化辅助
        // ══════════════════════════════════════════════════════════════

        private void InitCrosshairs()
        {
            _chFlow    = AttachCrosshair(plotFlow,        () => _chFlow);
            _chProfile = AttachCrosshair(plotProfile,     () => _chProfile);
            _chLong    = AttachCrosshair(plotLongProfile, () => _chLong);
            _chXs      = AttachCrosshair(plotXsShape,     () => _chXs);
            _chJunction     = AttachCrosshair(plotJunction,     () => _chJunction);
            _chTrib1Flow    = AttachCrosshair(plotTrib1Flow,    () => _chTrib1Flow);
            _chTrib1Profile = AttachCrosshair(plotTrib1Profile, () => _chTrib1Profile);
            _chTrib2Flow    = AttachCrosshair(plotTrib2Flow,    () => _chTrib2Flow);
            _chTrib2Profile = AttachCrosshair(plotTrib2Profile, () => _chTrib2Profile);
        }

        private static ScottPlot.Plottable.Crosshair AttachCrosshair(
            FormsPlot fp,
            Func<ScottPlot.Plottable.Crosshair?> getCh)
        {
            var ch = fp.Plot.AddCrosshair(0, 0);
            ch.IsVisible                    = false;
            ch.HorizontalLine.PositionLabel = true;
            ch.VerticalLine.PositionLabel   = true;
            ch.LineWidth                    = 1;
            ch.Color                        = Color.FromArgb(160, Color.DimGray);

            fp.MouseMove  += (s, e) =>
            {
                var cur = getCh(); if (cur == null) return;
                (double x, double y) = fp.Plot.GetCoordinate((float)e.X, (float)e.Y);
                cur.X = x; cur.Y = y; cur.IsVisible = true; fp.Refresh();
            };
            fp.MouseLeave += (s, e) =>
            {
                var cur = getCh(); if (cur == null) return;
                cur.IsVisible = false; fp.Refresh();
            };
            return ch;
        }

        private ScottPlot.Plottable.Crosshair ReAddCrosshair(FormsPlot fp)
        {
            var ch = fp.Plot.AddCrosshair(0, 0);
            ch.IsVisible                    = false;
            ch.HorizontalLine.PositionLabel = true;
            ch.VerticalLine.PositionLabel   = true;
            ch.LineWidth                    = 1;
            ch.Color                        = Color.FromArgb(160, Color.DimGray);
            if      (fp == plotFlow)         _chFlow         = ch;
            else if (fp == plotProfile)      _chProfile      = ch;
            else if (fp == plotLongProfile)  _chLong         = ch;
            else if (fp == plotXsShape)      _chXs           = ch;
            else if (fp == plotJunction)     _chJunction     = ch;
            else if (fp == plotTrib1Flow)    _chTrib1Flow    = ch;
            else if (fp == plotTrib1Profile) _chTrib1Profile = ch;
            else if (fp == plotTrib2Flow)    _chTrib2Flow    = ch;
            else if (fp == plotTrib2Profile) _chTrib2Profile = ch;
            return ch;
        }

        // ══════════════════════════════════════════════════════════════
        // 运行按钮可用状态管理
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// 仅当全部 6 组 CSV（支流1/2/干流的测点和索引）均已加载完毕时才启用"运行仿真"按钮。
        /// </summary>
        private void UpdateRunButton()
        {
            btnRun.Enabled =
                _xsMeasPts1 != null && _xsIndex1 != null &&
                _xsMeasPts2 != null && _xsIndex2 != null &&
                _xsMeasPts3 != null && _xsIndex3 != null;
        }

        /// <summary>
        /// 下游边界类型切换事件：更新水深标签和常驻说明文字。
        /// <para>
        /// 正常水深模式：说明"正常水深（m）≠坡度"，坡度已由断面数据自动推算；<br/>
        /// 固定水深模式：说明水深全程固定。
        /// </para>
        /// </summary>
        private void cmbDsBcType_SelectedIndexChanged(object sender, EventArgs e)
        {
            bool isNormal = cmbDsBcType.SelectedIndex == 0;
            lblDsDepthLabel.Text = isNormal
                ? "初始水深（m）（均匀流）："
                : "固定水深（m）：";
            lblDsBcInfo.Text = isNormal
                ? "ℹ 正常水深（m）≠ 坡度。坡度由断面索引中的桩号与床底高程自动推算；正常水深是在该坡度下满足曼宁公式的均匀流水深，仿真中随流量动态变化。\n⚠ 请勿将初始水深设为极小值（如 0.01 m）；请填入与流量相符的合理水深，否则仿真将因流速过高而失败。"
                : "ℹ 固定水深：出口水深在整个仿真中保持为所填数值（m），不随流量变化。";
        }

        /// <summary>
        /// 旁侧入流启用/禁用切换事件：根据复选框状态更新子控件的可用性。
        /// </summary>
        private void chkEnableLateral_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = chkEnableLateral.Checked;
            numLateralPeakFlow.Enabled = enabled;
            numLateralRiseTime.Enabled = enabled;
        }

        /// <summary>
        /// 旁侧出流启用/禁用切换事件：根据复选框状态更新子控件的可用性。
        /// </summary>
        private void chkEnableLateralOut_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = chkEnableLateralOut.Checked;
            numLateralOutPeakFlow.Enabled = enabled;
            numLateralOutRiseTime.Enabled = enabled;
        }

        /// <summary>
        /// 干流中游汇流启用/禁用切换事件：根据复选框状态更新子控件的可用性。
        /// </summary>
        /// <summary>
        /// 返回汇流断面下拉框中是否已有有效的（TaggedComboItem&lt;double&gt;）断面条目。
        /// 用于统一判断下拉框是否可操作。
        /// </summary>
        private bool HasValidJunctionSections =>
            cmbJunctionChainage1.Items.Count > 0
            && cmbJunctionChainage1.Items[0] is TaggedComboItem<double>;

        private void chkEnableMidJunction_CheckedChanged(object sender, EventArgs e)
        {
            bool enabled = chkEnableMidJunction.Checked;
            // 仅在已有候选断面时才真正启用下拉框
            cmbJunctionChainage1.Enabled = enabled && HasValidJunctionSections;
            cmbJunctionChainage2.Enabled = enabled && HasValidJunctionSections;
            numUpMainPeakFlow.Enabled   = enabled;
            numUpMainRiseTime.Enabled   = enabled;
        }



        // ══════════════════════════════════════════════════════════════
        // CSV 加载事件处理器
        // ══════════════════════════════════════════════════════════════

        private void btnLoadXsPts1_Click(object sender, EventArgs e) => LoadConfluencePts(ref _xsMeasPts1, lblXsPtsFile1, "支流1");
        private void btnLoadXsPts2_Click(object sender, EventArgs e) => LoadConfluencePts(ref _xsMeasPts2, lblXsPtsFile2, "支流2");
        private void btnLoadXsPts3_Click(object sender, EventArgs e) => LoadConfluencePts(ref _xsMeasPts3, lblXsPtsFile3, "干流");
        private void btnLoadXsIdx1_Click(object sender, EventArgs e) => LoadConfluenceIdx(ref _xsIndex1, lblXsIdxFile1, "支流1");
        private void btnLoadXsIdx2_Click(object sender, EventArgs e) => LoadConfluenceIdx(ref _xsIndex2, lblXsIdxFile2, "支流2");
        private void btnLoadXsIdx3_Click(object sender, EventArgs e)
        {
            LoadConfluenceIdx(ref _xsIndex3, lblXsIdxFile3, "干流");
            RefreshJunctionChainageCombo();
        }

        /// <summary>
        /// 加载干流断面索引后，重新填充汇流断面选择下拉框。
        /// 每一条记录格式："{断面名}  ({桩号} km)"，同时将桩号（m）作为 Tag 存储，
        /// 方便 <see cref="BuildJunctionSolver"/> 直接读取精确数值。
        /// </summary>
        private void RefreshJunctionChainageCombo()
        {
            cmbJunctionChainage1.Items.Clear();
            cmbJunctionChainage2.Items.Clear();

            if (_xsIndex3 == null || _xsIndex3.Count == 0)
            {
                cmbJunctionChainage1.Items.Add("（请先加载干流索引文件）");
                cmbJunctionChainage1.SelectedIndex = 0;
                cmbJunctionChainage1.Enabled = false;
                cmbJunctionChainage2.Items.Add("（请先加载干流索引文件）");
                cmbJunctionChainage2.SelectedIndex = 0;
                cmbJunctionChainage2.Enabled = false;
                return;
            }

            // 第一个断面（上游端）不能作为汇流点（汇流点上游至少需要一个断面）；
            // 最后一个断面（下游端）作为汇流点会导致下游段为空，同样无意义。
            // 因此可选范围为第 2 到倒数第 2 条记录（索引 1 … n-2）。
            int n = _xsIndex3.Count;
            int startIdx = n >= 2 ? 1 : 0;        // 至少保留上游一个断面
            int endIdx   = n >= 2 ? n - 1 : n;    // 至少保留下游一个断面（不含最后一个）

            for (int i = startIdx; i < endIdx; i++)
            {
                var rec = _xsIndex3[i];
                double chKm = rec.chainage / 1000.0;
                var item = new TaggedComboItem<double>(
                    $"{rec.name}  ({chKm:F2} km)",
                    rec.chainage);
                cmbJunctionChainage1.Items.Add(item);
                cmbJunctionChainage2.Items.Add(item);
            }

            if (cmbJunctionChainage1.Items.Count > 0)
            {
                // 默认：支流1选中上游1/3处，支流2选中下游2/3处（鼓励选择不同断面）
                int cnt = cmbJunctionChainage1.Items.Count;
                cmbJunctionChainage1.SelectedIndex = cnt / 3;
                cmbJunctionChainage2.SelectedIndex = cnt * 2 / 3;
            }
            else
            {
                cmbJunctionChainage1.Items.Add("（断面不足，无法设置中游汇流）");
                cmbJunctionChainage1.SelectedIndex = 0;
                cmbJunctionChainage2.Items.Add("（断面不足，无法设置中游汇流）");
                cmbJunctionChainage2.SelectedIndex = 0;
            }

            // 如果用户已勾选中游汇流，则启用下拉框
            bool enabled = chkEnableMidJunction.Checked && HasValidJunctionSections;
            cmbJunctionChainage1.Enabled = enabled;
            cmbJunctionChainage2.Enabled = enabled;
        }

        /// <summary>辅助：带 Tag 值的 ComboBox 条目。</summary>
        private sealed class TaggedComboItem<T>
        {
            public string Display { get; }
            public T Tag { get; }
            public TaggedComboItem(string display, T tag) { Display = display; Tag = tag; }
            public override string ToString() => Display;
        }

        private void LoadConfluencePts(
            ref System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>? target,
            Label lblFile,
            string channelLabel)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = $"选择{channelLabel}断面_测点 CSV 文件",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var pts = ParseXsPtsFile(dlg.FileName);
                int totalPts = 0;
                foreach (var kvp in pts) totalPts += kvp.Value.x.Length;
                target       = pts;
                lblFile.Text = System.IO.Path.GetFileName(dlg.FileName);
                Log($"[{channelLabel}] 已加载测点文件（{pts.Count} 个断面，共 {totalPts} 个测点）");
                DrawChannelLayout();
                UpdateRunButton();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载{channelLabel}测点 CSV 失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void LoadConfluenceIdx(
            ref System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>? target,
            Label lblFile,
            string channelLabel)
        {
            using var dlg = new OpenFileDialog
            {
                Title  = $"选择{channelLabel}断面_索引 CSV 文件",
                Filter = "CSV 文件 (*.csv)|*.csv|所有文件 (*.*)|*.*"
            };
            if (dlg.ShowDialog() != DialogResult.OK) return;
            try
            {
                var idx        = ParseXsIdxFile(dlg.FileName);
                int coordCount = idx.Count(r => r.x.HasValue);
                string coordInfo = coordCount > 0 ? $"，{coordCount} 个断面含坐标" : "";
                target       = idx;
                lblFile.Text = System.IO.Path.GetFileName(dlg.FileName);
                Log($"[{channelLabel}] 已加载索引文件（{idx.Count} 条记录{coordInfo}）");
                DrawChannelLayout();
                UpdateRunButton();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载{channelLabel}索引 CSV 失败：{ex.Message}", "错误",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        // ══════════════════════════════════════════════════════════════
        // CSV 解析
        // ══════════════════════════════════════════════════════════════

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
        // 运行仿真
        // ══════════════════════════════════════════════════════════════

        private void btnRun_Click(object sender, EventArgs e)
        {
            btnRun.Enabled  = false;
            btnSave.Enabled = false;
            txtLog.Clear();
            Log("正在构建支流汇流仿真...");

            try
            {
                var jSolver = BuildJunctionSolver();
                _junctionSolver = jSolver;

                int    totalSteps   = (int)((double)numSimTime.Value * 3600 / (double)numTimeStep.Value);
                int    logInterval  = Math.Max(1, totalSteps / 20);
                double timeStep     = (double)numTimeStep.Value;

                jSolver.LogCallback  = msg => Invoke(() => Log(msg));
                jSolver.StepCallback = (step, total, stepMs, totalSec) =>
                {
                    if (step % logInterval == 0 || step == total)
                    {
                        double pct    = total > 0 ? (double)step / total * 100.0 : 100.0;
                        double simHrs = step * timeStep / 3600.0;
                        string msg    = $"  [{pct,5:F1}%] 步骤 {step}/{total}，" +
                                        $"模拟时刻 {simHrs:F1} h，步时 {stepMs:F1} ms";
                        Invoke(() => Log(msg));
                    }
                };

                Log($"正在运行支流汇流仿真（每段约 {totalSteps} 步）…");
                Task.Run(() =>
                {
                    try
                    {
                        jSolver.Run(verbose: 0);
                        Invoke(() =>
                        {
                            _solver  = jSolver.MainSolver;
                            // _capturedSwapTribs tracks whether UI支流1 is at higher chainage than UI支流2;
                            // TributarySolvers are sorted by chainage (ascending), so:
                            //   swapTribs=false → [0]=支流1, [1]=支流2
                            //   swapTribs=true  → [0]=支流2, [1]=支流1
                            bool sw = _lastBuildSwapTribs;
                            _solver1 = jSolver.TributarySolvers.Count > 0
                                ? jSolver.TributarySolvers[sw ? 1 : 0] : null;
                            _solver2 = jSolver.TributarySolvers.Count > 1
                                ? jSolver.TributarySolvers[sw ? 0 : 1] : null;
                            string refinedNote = jSolver.IsRefined ? "（含精算迭代）" : "";
                            string stageNote = "";
                            if (jSolver.JunctionStage != null && jSolver.JunctionStage.Length > 0)
                            {
                                double minZ = jSolver.JunctionStage[0], maxZ = jSolver.JunctionStage[0];
                                for (int i = 1; i < jSolver.JunctionStage.Length; i++)
                                {
                                    if (jSolver.JunctionStage[i] < minZ) minZ = jSolver.JunctionStage[i];
                                    if (jSolver.JunctionStage[i] > maxZ) maxZ = jSolver.JunctionStage[i];
                                }
                                stageNote = $"，汇流点水位 {minZ:F2}～{maxZ:F2} m";
                            }
                            Log($"汇流仿真成功完成{refinedNote}，干流模拟时长 {_solver!.TotalSimDuration / 3600.0:F1} h{stageNote}。");
                            UpdateResults();
                            UpdateJunctionChart();
                            UpdateTributaryResults(_solver1, "支流1", plotTrib1Flow, plotTrib1Profile,
                                cmbTrib1FlowNode, cmbTrib1FlowNode_SelectedIndexChanged,
                                ref _chTrib1Flow, ref _chTrib1Profile);
                            UpdateTributaryResults(_solver2, "支流2", plotTrib2Flow, plotTrib2Profile,
                                cmbTrib2FlowNode, cmbTrib2FlowNode_SelectedIndexChanged,
                                ref _chTrib2Flow, ref _chTrib2Profile);
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

        private void btnSave_Click(object sender, EventArgs e)
        {
            if (_solver == null || !_solver.Solved) return;
            using var dlg = new FolderBrowserDialog { Description = "选择输出文件夹" };
            if (dlg.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    _solver.SaveResults(dlg.SelectedPath, "results_confluence.xlsx");
                    Log($"结果已保存至：{dlg.SelectedPath}");
                }
                catch (Exception ex)
                {
                    Log($"保存错误：{ex.Message}");
                }
            }
        }

        // ══════════════════════════════════════════════════════════════
        // 构造求解器
        // ══════════════════════════════════════════════════════════════

        private JunctionSolver BuildJunctionSolver()
        {
            // 前置校验（理论上 btnRun 已被 UpdateRunButton 保护，此处仅作防御）
            if (_xsMeasPts1 == null || _xsIndex1 == null ||
                _xsMeasPts2 == null || _xsIndex2 == null ||
                _xsMeasPts3 == null || _xsIndex3 == null)
                throw new InvalidOperationException(
                    "须先加载三组断面数据（支流1、支流2、干流的测点和索引 CSV）。");

            // 基流占峰值流量的比例（三角形过程线基流 = 峰值 × BaseFlowFraction）
            const double BaseFlowFraction = 0.1;

            double initialFlow1 = (double)numInitialFlow.Value;
            double initialFlow2 = (double)numInitialFlow.Value;
            double dsDepth      = (double)numDsDepth.Value;
            double timeStep     = (double)numTimeStep.Value;
            double spatialStep  = (double)numSpatialStep.Value;
            double simTime      = (double)numSimTime.Value * 3600;

            BoundaryConditionType dsBcType = cmbDsBcType.SelectedIndex == 1
                ? BoundaryConditionType.FixedDepth
                : BoundaryConditionType.NormalDepth;

            // ── 辅助：构造不规则断面 Channel ──
            Channel BuildIrregularChannel(
                System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)> xsIdx,
                System.Collections.Generic.Dictionary<string, (double[] x, double[] z)> xsPts,
                Boundary usBc,
                Boundary dsBc,
                double initFlow,
                string label)
            {
                // 归一化桩号：以第一个断面的桩号为原点，将绝对桩号转换为河段局部坐标。
                // 这使得子河段（如干流中游段、干流下游段）的河道长度等于实际段长，
                // 而非错误地使用最末断面的绝对桩号。
                double chOffset = xsIdx[0].chainage;
                double chLength = xsIdx[xsIdx.Count - 1].chainage - chOffset;
                double usBedLv  = xsPts.TryGetValue(xsIdx[0].name, out var fPts) ? fPts.z.Min() : 0;
                double dsBedLv  = xsPts.TryGetValue(xsIdx[xsIdx.Count - 1].name, out var lPts) ? lPts.z.Min() : 0;

                var usB = new Boundary(usBc.Condition, 0, usBedLv, null, null, usBc.Hydrograph);
                var dsB = new Boundary(dsBc.Condition, chLength, dsBedLv,
                    dsBc.InitialDepth ?? dsDepth, dsBc.RatingCurve, dsBc.Hydrograph);
                var ch  = new Channel(usB, dsB, initFlow, initMethod: InitializationMethod.GVFEquation);

                var chainageList = new System.Collections.Generic.List<double>();
                var sectionList  = new System.Collections.Generic.List<CrossSection>();
                var coordXs      = new System.Collections.Generic.List<double>();
                var coordYs      = new System.Collections.Generic.List<double>();
                var coordChs     = new System.Collections.Generic.List<double>();

                foreach (var rec in xsIdx)
                {
                    if (!xsPts.TryGetValue(rec.name, out var pts))
                    {
                        Log($"[{label}] 警告：断面「{rec.name}」在测点文件中未找到，已跳过。");
                        continue;
                    }
                    chainageList.Add(rec.chainage - chOffset);
                    sectionList.Add(new IrregularSection(pts.x, pts.z, rec.n));
                    if (rec.x.HasValue && rec.y.HasValue)
                    {
                        coordXs.Add(rec.x.Value); coordYs.Add(rec.y.Value); coordChs.Add(rec.chainage - chOffset);
                    }
                }

                if (chainageList.Count < 2)
                    throw new InvalidOperationException($"[{label}] 有效不规则断面不足（至少需要 2 个）。");

                ch.SetCrossSection(chainageList.ToArray(), sectionList.ToArray());

                if (coordChs.Count >= 2)
                {
                    int nc = coordChs.Count;
                    double[,] coords = new double[nc, 2];
                    for (int ci = 0; ci < nc; ci++) { coords[ci, 0] = coordXs[ci]; coords[ci, 1] = coordYs[ci]; }
                    ch.SetCoords(coords, coordChs.ToArray());
                }
                return ch;
            }

            // ── 辅助：构造求解器 ──
            // 提前在 UI 线程上读取所有控件值，避免 MainFactory 在后台线程调用时触发跨线程异常。
            string capturedSolverType = cmbSolverMethod.SelectedItem?.ToString() ?? "Preissmann";
            double capturedTheta      = (double)numTheta.Value;
            double capturedTolerance  = (double)numTolerance.Value;
            int    capturedMaxIter    = (int)numMaxIter.Value;

            Solver MakeSolver(Channel ch)
            {
                Solver s;
                if (capturedSolverType == "Lax-Friedrichs")
                    s = new LaxSolver(ch, timeStep, spatialStep, simTime);
                else
                    s = new PreissmannSolver(ch, capturedTheta, timeStep, spatialStep, simTime)
                        { Tolerance = capturedTolerance, MaxIterations = capturedMaxIter };

                // 验证初始流速，防止因初始水深过小导致 CFL 条件违反或迭代不收敛
                var ic = ch.InitialConditions;
                if (ic != null)
                {
                    int nn = ic.GetLength(0);
                    for (int i = 0; i < nn; i++)
                    {
                        double h  = ic[i, 0];
                        double Q  = Math.Abs(ic[i, 1]);
                        double hw = ch.BedLevelAt(i) + h;
                        double A  = ch.AreaAt(i, hw);
                        if (A > 0 && Q / A > 20.0)
                            throw new InvalidOperationException(
                                $"初始水深 {dsDepth:F2} m 与流量 {ch.InitialFlowRate:F0} m³/s 严重不匹配：\n" +
                                $"  节点 {i} 处初始流速 V ≈ {Q / A:F0} m/s，远超物理范围（>20 m/s）。\n" +
                                "  请增大下游初始水深（「初始水深」输入框），再重新运行。");
                    }
                }
                return s;
            }

            // ── 支流1 ──
            var hydro1 = BuildTriangularHydrograph((double)numPeakFlow1.Value, (double)numRiseTime1.Value * 3600, simTime);
            var usB1   = new Boundary(BoundaryConditionType.FlowHydrograph, 0, 0, null, null, hydro1);
            var dsB1   = new Boundary(BoundaryConditionType.NormalDepth,    0, 0, dsDepth);
            var ch1    = BuildIrregularChannel(_xsIndex1, _xsMeasPts1, usB1, dsB1, initialFlow1, "支流1");
            var s1     = MakeSolver(ch1);

            // ── 支流2 ──
            var hydro2 = BuildTriangularHydrograph((double)numPeakFlow2.Value, (double)numRiseTime2.Value * 3600, simTime);
            var usB2   = new Boundary(BoundaryConditionType.FlowHydrograph, 0, 0, null, null, hydro2);
            var dsB2   = new Boundary(BoundaryConditionType.NormalDepth,    0, 0, dsDepth);
            var ch2    = BuildIrregularChannel(_xsIndex2, _xsMeasPts2, usB2, dsB2, initialFlow2, "支流2");
            var s2     = MakeSolver(ch2);

            // ── 旁侧入流/出流（干流）：在 UI 线程读取控件值，供后台线程使用 ──
            bool   capturedLatEnabled      = chkEnableLateral.Checked;
            double capturedLatPeak         = (double)numLateralPeakFlow.Value;
            double capturedLatRiseTime     = (double)numLateralRiseTime.Value * 3600;
            bool   capturedLatOutEnabled   = chkEnableLateralOut.Checked;
            double capturedLatOutPeak      = (double)numLateralOutPeakFlow.Value;
            double capturedLatOutRiseTime  = (double)numLateralOutRiseTime.Value * 3600;

            // ── 汇流位置（干流中游）：在 UI 线程读取控件值 ──
            bool   capturedMidJunction    = chkEnableMidJunction.Checked;
            // 从下拉框的 Tag 获取精确桩号（m）；若未选或无有效项则为 -1
            double capturedJunctionCh1    = -1;
            double capturedJunctionCh2    = -1;
            if (capturedMidJunction)
            {
                if (cmbJunctionChainage1.SelectedItem is TaggedComboItem<double> tag1)
                    capturedJunctionCh1 = tag1.Tag;
                if (cmbJunctionChainage2.SelectedItem is TaggedComboItem<double> tag2)
                    capturedJunctionCh2 = tag2.Tag;
            }
            double capturedUpMainPeak     = (double)numUpMainPeakFlow.Value;
            double capturedUpMainRiseTime = (double)numUpMainRiseTime.Value * 3600;

            // ── 中游汇流模式验证 ──
            if (capturedMidJunction && (capturedJunctionCh1 < 0 || capturedJunctionCh2 < 0))
            {
                throw new InvalidOperationException(
                    "已勾选「支流汇入干流中游」，但尚未选择汇流断面。请先加载干流断面索引文件，然后在下拉框中选择汇流断面。");
            }

            // 两个汇流桩号：chLo = 上游（较小），chHi = 下游（较大）
            // 若两者差值 ≤ MinJunctionSeparationM，视为同一断面，退化为两段式
            const double MinJunctionSeparationM = 1.0;   // 1 m，小于此距离视为同一断面
            bool   isThreeSegment = capturedMidJunction && capturedJunctionCh1 > 0
                                    && Math.Abs(capturedJunctionCh1 - capturedJunctionCh2) > MinJunctionSeparationM;
            double chLo           = Math.Min(capturedJunctionCh1, capturedJunctionCh2);
            double chHi           = Math.Max(capturedJunctionCh1, capturedJunctionCh2);
            // 三段式时：ch1 < ch2 → s1 先汇；ch1 > ch2 → s2 先汇（需交换支流）
            bool   swapTribs      = isThreeSegment && capturedJunctionCh1 > capturedJunctionCh2;
            // firstTrib 在 chLo 汇入，secondTrib 在 chHi 汇入
            Solver  sFirst        = swapTribs ? s2 : s1;
            Solver  sSecond       = swapTribs ? s1 : s2;
            double  initFlowFirst = swapTribs ? initialFlow2 : initialFlow1;
            double  initFlowSecond= swapTribs ? initialFlow1 : initialFlow2;
            var xsIdxFirst        = swapTribs ? _xsIndex2 : _xsIndex1;
            var xsPtsFirst        = swapTribs ? _xsMeasPts2 : _xsMeasPts1;
            var usBFirst          = swapTribs ? usB2 : usB1;
            var xsIdxSecond       = swapTribs ? _xsIndex1 : _xsIndex2;
            var xsPtsSecond       = swapTribs ? _xsMeasPts1 : _xsMeasPts2;
            var usBSecond         = swapTribs ? usB1 : usB2;
            // 单汇口桩号（两段式用）
            double capturedJunctionCh = capturedMidJunction ? chLo : -1;

            // ── 干流上游段（仅在中游汇流模式下使用，桩号 ≤ chLo） ──
            Solver? upstreamMainSolver = null;
            // 精算工厂：干流上游段（中游汇流时才非 null）
            Func<Boundary, Solver>? upMainRefinedFactory = null;
            if (capturedMidJunction && capturedJunctionCh > 0)
            {
                // 从 _xsIndex3 中筛选出桩号 ≤ 汇流桩号的断面，构建干流上游段
                var upIdx = _xsIndex3!.FindAll(r => r.chainage <= capturedJunctionCh);
                if (upIdx.Count < 2)
                    throw new InvalidOperationException(
                        $"汇流断面（{capturedJunctionCh / 1000:F2} km）之前的干流断面不足（至少需要 2 个），请选择更靠下游的断面或检查断面索引数据。");

                var upHydro = BuildTriangularHydrograph(capturedUpMainPeak, capturedUpMainRiseTime, simTime);
                var usBUp   = new Boundary(BoundaryConditionType.FlowHydrograph, 0, 0, null, null, upHydro);
                var dsBUp   = new Boundary(BoundaryConditionType.NormalDepth, 0, 0, dsDepth);
                var chUp    = BuildIrregularChannel(upIdx, _xsMeasPts3!, usBUp, dsBUp, capturedUpMainPeak * BaseFlowFraction, "干流上游段");
                upstreamMainSolver = MakeSolver(chUp);
                Log($"[干流上游段] 中游汇流模式：第一汇口（{capturedJunctionCh / 1000:F2} km），干流上游段包含 {upIdx.Count} 个断面。");

                // 精算工厂：以汇流点水位作为干流上游段的下游边界，重建求解器
                double capturedUpInitFlow = capturedUpMainPeak * BaseFlowFraction;
                upMainRefinedFactory = refinedDsBC =>
                {
                    var chRefined = BuildIrregularChannel(upIdx, _xsMeasPts3!, usBUp, refinedDsBC, capturedUpInitFlow, "干流上游段（精算）");
                    return MakeSolver(chRefined);
                };
            }

            // ── 精算工厂：第一汇口支流（三段式中 = 先汇入的支流；两段式中 = 支流1）──
            Solver Trib1RefinedFactory(Boundary refinedDsBC)
            {
                var chRefined = BuildIrregularChannel(
                    swapTribs ? _xsIndex2 : _xsIndex1,
                    swapTribs ? _xsMeasPts2 : _xsMeasPts1,
                    swapTribs ? usB2 : usB1,
                    refinedDsBC,
                    swapTribs ? initialFlow2 : initialFlow1,
                    swapTribs ? "支流2（精算）" : "支流1（精算）");
                return MakeSolver(chRefined);
            }

            // ── 精算工厂：第二汇口支流 ──
            Solver Trib2RefinedFactory(Boundary refinedDsBC)
            {
                var chRefined = BuildIrregularChannel(
                    swapTribs ? _xsIndex1 : _xsIndex2,
                    swapTribs ? _xsMeasPts1 : _xsMeasPts2,
                    swapTribs ? usB1 : usB2,
                    refinedDsBC,
                    swapTribs ? initialFlow1 : initialFlow2,
                    swapTribs ? "支流1（精算）" : "支流2（精算）");
                return MakeSolver(chRefined);
            }

            // ── 三段式：干流中游段工厂（chLo → chHi，仅两汇口不同时存在）──
            Func<Hydrograph, Solver>? midMainSolverFactory = null;
            Func<Boundary, Solver>?  midMainRefinedFactory = null;
            if (isThreeSegment)
            {
                var midIdx = _xsIndex3!.FindAll(r => r.chainage >= chLo && r.chainage <= chHi);
                if (midIdx.Count < 2)
                    throw new InvalidOperationException(
                        $"两个汇流断面（{chLo / 1000:F2} km 和 {chHi / 1000:F2} km）之间的干流断面不足（至少需要 2 个），请选择断面间距更大的汇流位置。");

                double midInitFlow = (capturedMidJunction ? capturedUpMainPeak * BaseFlowFraction : 0) + initFlowFirst;
                Log($"[干流中游段] 三段式汇流：从 {chLo / 1000:F2} km 到 {chHi / 1000:F2} km，包含 {midIdx.Count} 个断面。");

                midMainSolverFactory = midHydro =>
                {
                    var usBMid   = new Boundary(BoundaryConditionType.FlowHydrograph, 0, 0, null, null, midHydro);
                    var dsBMid   = new Boundary(BoundaryConditionType.NormalDepth, 0, 0, dsDepth);
                    var chMid    = BuildIrregularChannel(midIdx, _xsMeasPts3!, usBMid, dsBMid, midInitFlow, "干流中游段");
                    return MakeSolver(chMid);
                };

                midMainRefinedFactory = refinedDsBC =>
                {
                    // 精算时以第二汇口水位（refinedDsBC）作为中游段下游边界；
                    // 上游入流使用与初算相同的三角型流量过程线（上游段基流 + 第一汇口支流峰值估算）。
                    var usBMidR = new Boundary(BoundaryConditionType.FlowHydrograph, 0, 0, null, null,
                        BuildTriangularHydrograph(capturedUpMainPeak + initFlowFirst, capturedUpMainRiseTime, simTime));
                    var chMidR  = BuildIrregularChannel(midIdx, _xsMeasPts3!, usBMidR, refinedDsBC, midInitFlow, "干流中游段（精算）");
                    return MakeSolver(chMidR);
                };
            }

            // ── 干流工厂（下游段，或当无中游汇流时为全程干流） ──
            Solver MainFactory(Hydrograph combinedHydro)
            {
                // 选取干流下游段断面
                List<(string name, double chainage, double n, double? x, double? y, string remark)> mainIdx;
                double dsJunctionCh = isThreeSegment ? chHi : (capturedMidJunction && capturedJunctionCh > 0 ? capturedJunctionCh : -1);
                mainIdx = dsJunctionCh > 0
                    ? _xsIndex3!.FindAll(r => r.chainage >= dsJunctionCh)
                    : _xsIndex3!;

                if (mainIdx.Count < 2)
                    throw new InvalidOperationException(
                        $"最下游汇流断面（{dsJunctionCh / 1000:F2} km）之后的干流断面不足（至少需要 2 个），请选择更靠上游的断面或检查断面索引数据。");

                double mainInitFlow;
                if (isThreeSegment)
                    mainInitFlow = capturedUpMainPeak * BaseFlowFraction + initFlowFirst + initFlowSecond;
                else if (capturedMidJunction && capturedJunctionCh > 0)
                    mainInitFlow = capturedUpMainPeak * BaseFlowFraction + initialFlow1 + initialFlow2;
                else
                    mainInitFlow = initialFlow1 + initialFlow2;

                var usBMain = new Boundary(BoundaryConditionType.FlowHydrograph, 0, 0, null, null, combinedHydro);
                var dsBMain = new Boundary(dsBcType, 0, 0, dsDepth);
                var chMain  = BuildIrregularChannel(mainIdx, _xsMeasPts3!, usBMain, dsBMain, mainInitFlow, "干流");

                // 如果启用了旁侧入流，将其附加到干流信道
                if (capturedLatEnabled && capturedLatPeak > 0 && capturedLatRiseTime > 0)
                {
                    var latHydro = BuildTriangularHydrograph(capturedLatPeak, capturedLatRiseTime, simTime);
                    chMain.SetLateralInflow(latHydro);
                    Log($"[干流] 旁侧入流已启用：峰值 {capturedLatPeak:F0} m³/s，起涨时间 {capturedLatRiseTime / 3600:F1} h");
                }

                // 如果启用了旁侧出流，将其附加到干流信道
                if (capturedLatOutEnabled && capturedLatOutPeak > 0 && capturedLatOutRiseTime > 0)
                {
                    var latOutHydro = BuildTriangularHydrograph(capturedLatOutPeak, capturedLatOutRiseTime, simTime);
                    chMain.SetLateralOutflow(latOutHydro);
                    Log($"[干流] 旁侧出流已启用：峰值 {capturedLatOutPeak:F0} m³/s，起涨时间 {capturedLatOutRiseTime / 3600:F1} h");
                }

                return MakeSolver(chMain);
            }

            // ── 记录本次构建的支流交换状态，供仿真完成后正确映射结果选项卡 ──
            _lastBuildSwapTribs = swapTribs;

            // ── 构造 TributaryEntry 列表（按汇流桩号排序）──
            // 注意：JunctionSolver 内部会再次排序；此处顺序仅供阅读。
            var trib1Entry = new TributaryEntry(sFirst,  chLo > 0 ? chLo : 0, Trib1RefinedFactory);
            var trib2Entry = new TributaryEntry(sSecond, isThreeSegment ? chHi : (chLo > 0 ? chLo : 0), Trib2RefinedFactory);

            // ── 构造干流段工厂列表 ──
            // 三段式（不同汇口）：segFactories = [midMainSolverFactory, MainFactory]
            // 两段式（相同汇口）：segFactories = [MainFactory]
            Func<Hydrograph, Solver>[] segFactories;
            Func<Boundary, Solver>?[] refinedSegFactories;

            if (isThreeSegment)
            {
                segFactories        = new Func<Hydrograph, Solver>[] { midMainSolverFactory!, MainFactory };
                refinedSegFactories = new Func<Boundary, Solver>?[]  { midMainRefinedFactory };
            }
            else
            {
                segFactories        = new Func<Hydrograph, Solver>[] { MainFactory };
                refinedSegFactories = System.Array.Empty<Func<Boundary, Solver>?>();
            }

            return new JunctionSolver(
                tributaries:             new[] { trib1Entry, trib2Entry },
                segmentFactories:        segFactories,
                upstreamMainSolver:      upstreamMainSolver,
                upMainRefinedFactory:    upMainRefinedFactory,
                refinedSegmentFactories: refinedSegFactories);
        }

        private static Hydrograph BuildTriangularHydrograph(double peakFlow, double riseTime, double totalTime)
        {
            double baseFlow = peakFlow * 0.1;
            double fallTime = Math.Min(riseTime * 2, totalTime - riseTime);
            return new Hydrograph(t =>
            {
                if (t <= riseTime)
                    return baseFlow + (peakFlow - baseFlow) * t / riseTime;
                if (t <= riseTime + fallTime)
                    return peakFlow - (peakFlow - baseFlow) * (t - riseTime) / fallTime;
                return baseFlow;
            });
        }

        // ══════════════════════════════════════════════════════════════
        // 结果展示
        // ══════════════════════════════════════════════════════════════

        private void UpdateResults()
        {
            if (_solver == null || !_solver.Solved) return;

            int    nk       = _solver.TimeLevel + 1;
            int    nn       = _solver.NumberOfNodes;
            double dt       = _solver.TimeStep;
            double[] distance = _solver.Channel.ChAtNode!;

            // ── 填充断面选择下拉框 ──
            cmbFlowNode.SelectedIndexChanged -= cmbFlowNode_SelectedIndexChanged;
            cmbFlowNode.Items.Clear();
            cmbFlowNode.Items.Add("全部代表节点");
            for (int i = 0; i < nn; i++)
                cmbFlowNode.Items.Add($"节点 {i}（{distance[i] / 1000.0:F1} km）");
            cmbFlowNode.Enabled      = true;
            cmbFlowNode.SelectedIndex = 0;
            cmbFlowNode.SelectedIndexChanged += cmbFlowNode_SelectedIndexChanged;

            // ── 流量过程线 ──
            DrawFlowChartFor(plotFlow, _solver, cmbFlowNode, ref _chFlow, "干流流量过程线");

            // ── 峰值水面纵剖面 ──
            plotProfile.Plot.Clear();
            _chProfile = ReAddCrosshair(plotProfile);
            int peakTimeIndex = 0;
            double peakQ = 0;
            for (int k = 0; k < nk; k++)
                if (_solver.Flow![k, 0] > peakQ) { peakQ = _solver.Flow[k, 0]; peakTimeIndex = k; }

            double[] levels = new double[nn];
            double[] bed    = _solver.BedProfile!;
            for (int i = 0; i < nn; i++) levels[i] = _solver.Level![peakTimeIndex, i];

            var distKm = Array.ConvertAll(distance, d => d / 1000.0);
            var wlScatter  = plotProfile.Plot.AddScatter(distKm, levels, label: "峰值水位");
            wlScatter.Color = Color.Blue; wlScatter.MarkerSize = 0;
            var bedScatter = plotProfile.Plot.AddScatter(distKm, bed, label: "床底高程");
            bedScatter.Color = Color.SaddleBrown; bedScatter.MarkerSize = 0;
            plotProfile.Plot.XLabel("距离（km）");
            plotProfile.Plot.YLabel("高程（m）");
            plotProfile.Plot.Title("峰值水面纵剖面");
            plotProfile.Plot.Legend();
            plotProfile.Refresh();

            // ── 统计汇总 ──
            double peakIn = 0, peakOut = 0, sumQin = 0, sumQout = 0, massImbVol = 0;
            for (int k = 0; k < nk; k++)
            {
                double qIn  = _solver.Flow![k, 0];
                double qOut = _solver.Flow![k, nn - 1];
                peakIn     = Math.Max(peakIn, qIn);
                peakOut    = Math.Max(peakOut, qOut);
                sumQin    += qIn;
                sumQout   += qOut;
                massImbVol += (qIn - qOut) * dt;
            }
            double atten           = peakIn > 0 ? (peakIn - peakOut) / peakIn * 100 : 0;
            double totalInflowVol  = sumQin  * dt;
            double totalOutflowVol = sumQout * dt;
            double massImbPct      = totalInflowVol > 0 ? massImbVol / totalInflowVol * 100 : 0;

            gridSummary.Rows.Clear();
            gridSummary.Rows.Add("空间步长（m）",         $"{_solver.SpatialStep:F1}");
            gridSummary.Rows.Add("时间步长（s）",         $"{_solver.TimeStep:F1}");
            gridSummary.Rows.Add("节点数量",              _solver.NumberOfNodes);
            gridSummary.Rows.Add("时间步数",              _solver.TimeLevel + 1);
            gridSummary.Rows.Add("峰值入流（m³/s）",     $"{peakIn:F2}");
            gridSummary.Rows.Add("峰值出流（m³/s）",     $"{peakOut:F2}");
            gridSummary.Rows.Add("洪峰削减率（%）",      $"{atten:F2}");
            gridSummary.Rows.Add("总入流量（万m³）",      $"{totalInflowVol / 10000.0:F2}");
            gridSummary.Rows.Add("总出流量（万m³）",      $"{totalOutflowVol / 10000.0:F2}");
            gridSummary.Rows.Add("净蓄水量变化（万m³）",  $"{massImbVol / 10000.0:F2}");
            gridSummary.Rows.Add("质量不平衡（%）",      $"{massImbPct:F4}");

            gridCfl.Rows.Clear();
            if (_solver is LaxSolver laxSolver && laxSolver.MaxCflPerStep != null)
            {
                double maxCflAll = 0;
                for (int k = 1; k < nk; k++)
                {
                    double cfl = laxSolver.MaxCflPerStep[k];
                    maxCflAll  = Math.Max(maxCflAll, cfl);
                    gridCfl.Rows.Add(k, $"{k * dt / 3600.0:F3}", $"{cfl:F4}");
                    if (cfl > 1.0)
                        gridCfl.Rows[gridCfl.Rows.Count - 1].DefaultCellStyle.BackColor = Color.LightSalmon;
                }
                gridSummary.Rows.Add("最大 CFL 数", $"{maxCflAll:F4}");
            }
            else
            {
                gridCfl.Rows.Add("—", "—", "（仅 Lax-Friedrichs 格式显示 CFL）");
            }

            UpdateChartsTab();
            UpdateDataTab();
        }

        /// <summary>
        /// 在"汇流水文"选项卡中绘制三段流量过程线：支流1出口、支流2出口、以及叠加后的干流入口。
        /// </summary>
        private void UpdateJunctionChart()
        {
            if (_solver1 == null || !_solver1.Solved) return;
            if (_solver2 == null || !_solver2.Solved) return;
            if (_solver  == null || !_solver.Solved)  return;

            plotJunction.Plot.Clear();
            _chJunction = ReAddCrosshair(plotJunction);

            int    nk1 = _solver1.TimeLevel + 1;
            int    nk2 = _solver2.TimeLevel + 1;
            int    nk  = _solver.TimeLevel  + 1;
            double dt  = _solver1.TimeStep;

            int    nn1 = _solver1.NumberOfNodes;
            int    nn2 = _solver2.NumberOfNodes;

            int nMin = Math.Min(Math.Min(nk1, nk2), nk);
            double[] times    = new double[nMin];
            double[] q1Out    = new double[nMin];
            double[] q2Out    = new double[nMin];
            double[] qCombined = new double[nMin];

            for (int k = 0; k < nMin; k++)
            {
                times[k]     = k * dt / 3600.0;
                q1Out[k]     = _solver1.Flow![k, nn1 - 1];
                q2Out[k]     = _solver2.Flow![k, nn2 - 1];
                qCombined[k] = q1Out[k] + q2Out[k];
            }

            var s1 = plotJunction.Plot.AddScatter(times, q1Out, label: "支流1出口");
            s1.Color = Color.DodgerBlue; s1.MarkerSize = 0; s1.LineWidth = 1.5f;

            var s2 = plotJunction.Plot.AddScatter(times, q2Out, label: "支流2出口");
            s2.Color = Color.LimeGreen; s2.MarkerSize = 0; s2.LineWidth = 1.5f;

            var sc = plotJunction.Plot.AddScatter(times, qCombined, label: "汇口入流（干流上游）");
            sc.Color = Color.Crimson; sc.MarkerSize = 0; sc.LineWidth = 2;

            plotJunction.Plot.XLabel("时间（h）");
            plotJunction.Plot.YLabel("流量（m³/s）");
            plotJunction.Plot.Title("汇口流量过程线（支流1 + 支流2 → 干流）");
            plotJunction.Plot.Legend();
            plotJunction.Refresh();
        }

        /// <summary>
        /// 填充支流1或支流2的结果选项卡（流量过程线 + 峰值水面纵剖面）。
        /// </summary>
        private void UpdateTributaryResults(
            Solver?   solver,
            string    label,
            FormsPlot fpFlow,
            FormsPlot fpProfile,
            ComboBox  cmbNode,
            EventHandler nodeChangedHandler,
            ref ScottPlot.Plottable.Crosshair? chFlow,
            ref ScottPlot.Plottable.Crosshair? chProfile)
        {
            if (solver == null || !solver.Solved) return;

            int    nk = solver.TimeLevel + 1;
            int    nn = solver.NumberOfNodes;
            double[] distance = solver.Channel.ChAtNode!;

            // 填充断面选择下拉框
            cmbNode.SelectedIndexChanged -= nodeChangedHandler;
            cmbNode.Items.Clear();
            cmbNode.Items.Add("全部代表节点");
            for (int i = 0; i < nn; i++)
                cmbNode.Items.Add($"节点 {i}（{distance[i] / 1000.0:F1} km）");
            cmbNode.Enabled      = true;
            cmbNode.SelectedIndex = 0;
            cmbNode.SelectedIndexChanged += nodeChangedHandler;

            // 流量过程线
            DrawFlowChartFor(fpFlow, solver, cmbNode, ref chFlow, $"{label}流量过程线");

            // 峰值水面纵剖面
            fpProfile.Plot.Clear();
            chProfile = ReAddCrosshair(fpProfile);
            int peakTimeIndex = 0;
            double peakQ = 0;
            for (int k = 0; k < nk; k++)
                if (solver.Flow![k, 0] > peakQ) { peakQ = solver.Flow[k, 0]; peakTimeIndex = k; }

            double[] bed    = solver.BedProfile!;
            double[] levels = new double[nn];
            for (int i = 0; i < nn; i++) levels[i] = solver.Level![peakTimeIndex, i];
            var distKm = Array.ConvertAll(distance, d => d / 1000.0);

            var wlScatter  = fpProfile.Plot.AddScatter(distKm, levels, label: "峰值水位");
            wlScatter.Color = Color.Blue; wlScatter.MarkerSize = 0;
            var bedScatter = fpProfile.Plot.AddScatter(distKm, bed, label: "床底高程");
            bedScatter.Color = Color.SaddleBrown; bedScatter.MarkerSize = 0;
            fpProfile.Plot.XLabel("距离（km）");
            fpProfile.Plot.YLabel("高程（m）");
            fpProfile.Plot.Title($"{label}峰值水面纵剖面");
            fpProfile.Plot.Legend();
            fpProfile.Refresh();
        }

        /// <summary>
        /// 根据 <paramref name="cmbNode"/> 当前选项绘制流量过程线图表。
        /// 选项 0（"全部代表节点"）显示上游/中部/下游三条曲线；其余选项对应单个节点。
        /// </summary>
        private void DrawFlowChartFor(
            FormsPlot fpFlow,
            Solver solver,
            ComboBox cmbNode,
            ref ScottPlot.Plottable.Crosshair? chFlow,
            string baseTitle)
        {
            int    nk = solver.TimeLevel + 1;
            int    nn = solver.NumberOfNodes;
            double dt = solver.TimeStep;
            double[] chainages = solver.Channel.ChAtNode!;

            fpFlow.Plot.Clear();
            chFlow = ReAddCrosshair(fpFlow);

            double[] times = new double[nk];
            for (int k = 0; k < nk; k++) times[k] = k * dt / 3600.0;

            int selectedIdx = cmbNode.SelectedIndex;
            if (selectedIdx <= 0)
            {
                int[]    plotNodes  = { 0, nn / 2, nn - 1 };
                string[] nodeLabels = { "上游", "中部", "下游" };
                var      colors     = new[] { Color.Blue, Color.Green, Color.Red };
                for (int n = 0; n < plotNodes.Length; n++)
                {
                    int ni = plotNodes[n];
                    double[] qs = new double[nk];
                    for (int k = 0; k < nk; k++) qs[k] = solver.Flow![k, ni];
                    var scatter = fpFlow.Plot.AddScatter(times, qs, label: nodeLabels[n]);
                    scatter.Color = colors[n]; scatter.MarkerSize = 0;
                }
                fpFlow.Plot.Title(baseTitle);
            }
            else
            {
                int ni = selectedIdx - 1;
                double[] qs = new double[nk];
                for (int k = 0; k < nk; k++) qs[k] = solver.Flow![k, ni];
                string label = $"节点 {ni}（{chainages[ni] / 1000.0:F1} km）";
                var scatter = fpFlow.Plot.AddScatter(times, qs, label: label);
                scatter.Color = Color.SteelBlue; scatter.MarkerSize = 0;
                fpFlow.Plot.Title($"{baseTitle} — {label}");
            }

            fpFlow.Plot.XLabel("时间（h）");
            fpFlow.Plot.YLabel("流量（m³/s）");
            fpFlow.Plot.Legend();
            fpFlow.Refresh();
        }

        private void cmbFlowNode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_solver == null || !_solver.Solved) return;
            DrawFlowChartFor(plotFlow, _solver, cmbFlowNode, ref _chFlow, "干流流量过程线");
        }

        private void cmbTrib1FlowNode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_solver1 == null || !_solver1.Solved) return;
            DrawFlowChartFor(plotTrib1Flow, _solver1, cmbTrib1FlowNode, ref _chTrib1Flow, "支流1流量过程线");
        }

        private void cmbTrib2FlowNode_SelectedIndexChanged(object? sender, EventArgs e)
        {
            if (_solver2 == null || !_solver2.Solved) return;
            DrawFlowChartFor(plotTrib2Flow, _solver2, cmbTrib2FlowNode, ref _chTrib2Flow, "支流2流量过程线");
        }

        private void UpdateChartsTab()
        {
            if (_solver == null || !_solver.Solved) return;

            int    nk       = _solver.TimeLevel + 1;
            int    nn       = _solver.NumberOfNodes;
            double[] chainages = _solver.Channel.ChAtNode!;

            trkLongTime.Minimum = 0; trkLongTime.Maximum = nk - 1;
            trkLongTime.Value   = 0; trkLongTime.TickFrequency = Math.Max(1, (nk - 1) / 20);
            trkLongTime.Enabled = true;

            trkXsTime.Minimum = 0; trkXsTime.Maximum = nk - 1;
            trkXsTime.Value   = 0; trkXsTime.TickFrequency = Math.Max(1, (nk - 1) / 20);
            trkXsTime.Enabled = true;

            cmbXsNode.Items.Clear();
            for (int i = 0; i < nn; i++)
                cmbXsNode.Items.Add($"节点 {i}（{chainages[i] / 1000.0:F1} km）");
            cmbXsNode.Enabled = true;

            cmbXsNode.SelectedIndexChanged -= cmbXsNode_SelectedIndexChanged;
            cmbXsNode.SelectedIndex = nn / 2;
            cmbXsNode.SelectedIndexChanged += cmbXsNode_SelectedIndexChanged;

            UpdateLongProfile();
            UpdateXsChart();
        }

        private void UpdateDataTab()
        {
            if (_solver == null || !_solver.Solved) return;
            int nk = _solver.TimeLevel + 1;
            trkDataTime.Minimum = 0; trkDataTime.Maximum = nk - 1;
            trkDataTime.Value   = 0; trkDataTime.TickFrequency = Math.Max(1, (nk - 1) / 20);
            trkDataTime.Enabled = true;
            UpdateDataTable();
        }

        private void UpdateDataTable()
        {
            if (_solver == null || !_solver.Solved) return;
            int    k       = trkDataTime.Value;
            double tHrs    = k * _solver.TimeStep / 3600.0;
            lblDataTime.Text = $"{tHrs:F1} 小时";

            int    nn       = _solver.NumberOfNodes;
            double[] chainages = _solver.Channel.ChAtNode!;

            gridData.Rows.Clear();
            for (int i = 0; i < nn; i++)
            {
                double q  = _solver.Flow![k, i];
                double wl = _solver.Level![k, i];
                double h  = _solver.Depth![k, i];
                double a  = _solver.Area      != null ? _solver.Area[k, i]        : double.NaN;
                double tw = _solver.TopWidth   != null ? _solver.TopWidth[k, i]    : double.NaN;
                double v  = _solver.Velocity   != null ? _solver.Velocity[k, i]   : double.NaN;
                double fr = _solver.FroudeNumber != null ? _solver.FroudeNumber[k, i] : double.NaN;

                gridData.Rows.Add(
                    i,
                    $"{chainages[i] / 1000.0:F3}",
                    $"{q:F3}", $"{wl:F3}", $"{h:F3}",
                    double.IsNaN(a)  ? "—" : $"{a:F2}",
                    double.IsNaN(tw) ? "—" : $"{tw:F2}",
                    double.IsNaN(v)  ? "—" : $"{v:F3}",
                    double.IsNaN(fr) ? "—" : $"{fr:F4}");
            }
        }

        private void trkDataTime_Scroll(object? sender, EventArgs e) => UpdateDataTable();

        private void UpdateLongProfile()
        {
            if (_solver == null || !_solver.Solved) return;
            int    k    = trkLongTime.Value;
            double tHrs = k * _solver.TimeStep / 3600.0;
            lblLongTime.Text = $"{tHrs:F1} 小时";

            int    nn     = _solver.NumberOfNodes;
            double[] dist = _solver.Channel.ChAtNode!;
            var distKm    = Array.ConvertAll(dist, d => d / 1000.0);
            double[] wl   = new double[nn];
            for (int i = 0; i < nn; i++) wl[i] = _solver.Level![k, i];

            plotLongProfile.Plot.Clear();
            _chLong = ReAddCrosshair(plotLongProfile);

            var bedLine = plotLongProfile.Plot.AddScatter(distKm, _solver.BedProfile!, label: "床底");
            bedLine.Color = Color.SaddleBrown; bedLine.MarkerSize = 0; bedLine.LineWidth = 1.5f;

            var wlLine = plotLongProfile.Plot.AddScatter(distKm, wl, label: "水面");
            wlLine.Color = Color.DodgerBlue; wlLine.MarkerSize = 0; wlLine.LineWidth = 2;

            plotLongProfile.Plot.XLabel("距离（km）");
            plotLongProfile.Plot.YLabel("高程（m）");
            plotLongProfile.Plot.Title($"纵断面水位（t = {tHrs:F1} h）");
            plotLongProfile.Plot.Legend();
            plotLongProfile.Refresh();
        }

        private void UpdateXsChart()
        {
            if (_solver == null || !_solver.Solved) return;
            int nodeIdx = cmbXsNode.SelectedIndex;
            if (nodeIdx < 0) return;

            int    k       = trkXsTime.Value;
            double tHrs    = k * _solver.TimeStep / 3600.0;
            lblXsTime.Text = $"{tHrs:F1} 小时";

            var    xs       = _solver.Channel.XsAtNode![nodeIdx];
            double wl       = _solver.Level![k, nodeIdx];
            double depth    = wl - xs.ZMin;
            double maxDepth = Math.Max(depth + 1.0, 2.0);

            var (xPts, zPts) = xs.GetDisplayShape(maxDepth);

            plotXsShape.Plot.Clear();
            _chXs = ReAddCrosshair(plotXsShape);

            try
            {
                var terrain = plotXsShape.Plot.AddPolygon(xPts, zPts);
                terrain.FillColor = Color.FromArgb(200, Color.SandyBrown);
                terrain.LineColor  = Color.SaddleBrown;
                terrain.LineWidth  = 1.5f;
            }
            catch (Exception ex)
            {
                Log($"地形多边形绘制失败（{ex.GetType().Name}），改用折线绘制。");
                var terrainLine = plotXsShape.Plot.AddScatter(xPts, zPts);
                terrainLine.Color = Color.SaddleBrown; terrainLine.MarkerSize = 0; terrainLine.LineWidth = 2;
            }

            if (depth > 0)
            {
                double xLeft  = xPts[0];
                double xRight = xPts[xPts.Length - 1];
                var wlLine = plotXsShape.Plot.AddScatter(
                    new[] { xLeft, xRight }, new[] { wl, wl }, label: $"水位 {wl:F2} m");
                wlLine.Color = Color.DodgerBlue; wlLine.MarkerSize = 0; wlLine.LineWidth = 2.5f;
            }

            double chainage = _solver.Channel.ChAtNode![nodeIdx];
            plotXsShape.Plot.XLabel("断面横坐标（m）");
            plotXsShape.Plot.YLabel("高程（m）");
            plotXsShape.Plot.Title($"断面形状（桩号 {chainage / 1000.0:F1} km，t = {tHrs:F1} h，水深 {Math.Max(depth, 0):F2} m）");
            plotXsShape.Plot.Legend();
            plotXsShape.Refresh();
        }

        private void trkLongTime_Scroll(object? sender, EventArgs e) => UpdateLongProfile();
        private void trkXsTime_Scroll(object? sender, EventArgs e)   => UpdateXsChart();
        private void cmbXsNode_SelectedIndexChanged(object? sender, EventArgs e) => UpdateXsChart();

        // ══════════════════════════════════════════════════════════════
        // 平面图
        // ══════════════════════════════════════════════════════════════

        private void DrawChannelLayout()
        {
            plotChannelLayout.Plot.Clear();
            bool anyDrawn = false;

            anyDrawn |= DrawSingleChannelOnLayout("支流1", Color.DodgerBlue,  _xsIndex1, _xsMeasPts1);
            anyDrawn |= DrawSingleChannelOnLayout("支流2", Color.LimeGreen,   _xsIndex2, _xsMeasPts2);
            anyDrawn |= DrawSingleChannelOnLayout("干流",  Color.Crimson,     _xsIndex3, _xsMeasPts3);
            TryMarkJunction();

            if (!anyDrawn)
            {
                plotChannelLayout.Plot.Title("河道平面布置图（请加载支流和干流断面文件）");
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

        private bool DrawSingleChannelOnLayout(
            string channelLabel,
            Color  channelColor,
            System.Collections.Generic.List<(string name, double chainage, double n, double? x, double? y, string remark)>? xsIndex,
            System.Collections.Generic.Dictionary<string, (double[] x, double[] z)>? xsMeasPts)
        {
            if (xsIndex == null) return false;

            var coordSecs = new System.Collections.Generic.List<(string name, double x, double y)>();
            foreach (var rec in xsIndex)
                if (rec.x.HasValue && rec.y.HasValue)
                    coordSecs.Add((rec.name, rec.x.Value, rec.y.Value));

            if (coordSecs.Count < 2) return false;

            int n = coordSecs.Count;
            double[] clX = new double[n];
            double[] clY = new double[n];
            for (int i = 0; i < n; i++) { clX[i] = coordSecs[i].x; clY[i] = coordSecs[i].y; }

            var cl = plotChannelLayout.Plot.AddScatter(clX, clY, label: channelLabel);
            cl.Color = channelColor; cl.LineWidth = 2; cl.MarkerSize = 5;
            cl.MarkerShape = ScottPlot.MarkerShape.filledCircle;

            double totalLen = 0;
            for (int i = 1; i < n; i++)
                totalLen += Math.Sqrt(Math.Pow(clX[i] - clX[i - 1], 2) + Math.Pow(clY[i] - clY[i - 1], 2));
            double defaultHalfWidth = totalLen / (n - 1) * 0.4;

            bool isFirstStub = true;
            for (int i = 0; i < n; i++)
            {
                double tx, ty;
                if      (i == 0)     { tx = clX[1] - clX[0]; ty = clY[1] - clY[0]; }
                else if (i == n - 1) { tx = clX[n - 1] - clX[n - 2]; ty = clY[n - 1] - clY[n - 2]; }
                else                 { tx = clX[i + 1] - clX[i - 1]; ty = clY[i + 1] - clY[i - 1]; }

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
                stub.Color = channelColor; stub.LineWidth = 1.5f; stub.MarkerSize = 0;
                stub.LineStyle = ScottPlot.LineStyle.Solid;

                var txt = plotChannelLayout.Plot.AddText(
                    coordSecs[i].name,
                    clX[i] + halfWidth * normX * 1.15,
                    clY[i] + halfWidth * normY * 1.15);
                txt.FontSize = 7; txt.Color = channelColor;
            }
            return true;
        }

        private void TryMarkJunction()
        {
            // 当中游汇流模式启用且干流断面索引已加载时，用选中的汇流断面桩号查找坐标
            if (chkEnableMidJunction.Checked && _xsIndex3 != null)
            {
                MarkJunctionFromCombo(cmbJunctionChainage1, "汇口1", Color.DarkViolet);
                MarkJunctionFromCombo(cmbJunctionChainage2, "汇口2", Color.OrangeRed);
                return;
            }

            // 未启用中游汇流时，退回到原有逻辑：取支流1末端或干流首断面坐标
            double? jx = null, jy = null;
            if (_xsIndex1 != null)
            {
                var last = _xsIndex1[_xsIndex1.Count - 1];
                if (last.x.HasValue && last.y.HasValue) { jx = last.x; jy = last.y; }
            }
            if (jx == null && _xsIndex3 != null)
            {
                var first = _xsIndex3[0];
                if (first.x.HasValue && first.y.HasValue) { jx = first.x; jy = first.y; }
            }
            if (jx == null || jy == null) return;

            var jPt = plotChannelLayout.Plot.AddScatter(new[] { jx.Value }, new[] { jy.Value }, label: "汇口");
            jPt.Color = Color.DarkViolet; jPt.MarkerSize = 12; jPt.LineWidth = 0;
            jPt.MarkerShape = ScottPlot.MarkerShape.filledCircle;

            var jTxt = plotChannelLayout.Plot.AddText("汇口", jx.Value + 50, jy.Value + 50);
            jTxt.FontSize = 9; jTxt.Color = Color.DarkViolet;
        }

        /// <summary>
        /// 根据 ComboBox 中选定的汇流断面桩号，在平面图上标注汇流点。
        /// </summary>
        private void MarkJunctionFromCombo(ComboBox cmb, string label, Color color)
        {
            if (_xsIndex3 == null) return;
            if (!(cmb.SelectedItem is TaggedComboItem<double> tagItem)) return;

            double targetCh = tagItem.Tag;
            // 在干流断面索引中查找最近的有坐标断面
            double? jx = null, jy = null;
            double minDist = double.MaxValue;
            foreach (var rec in _xsIndex3)
            {
                if (!rec.x.HasValue || !rec.y.HasValue) continue;
                double dist = Math.Abs(rec.chainage - targetCh);
                if (dist < minDist) { minDist = dist; jx = rec.x; jy = rec.y; }
            }
            if (jx == null || jy == null) return;

            var jPt = plotChannelLayout.Plot.AddScatter(new[] { jx.Value }, new[] { jy.Value }, label: label);
            jPt.Color = color; jPt.MarkerSize = 12; jPt.LineWidth = 0;
            jPt.MarkerShape = ScottPlot.MarkerShape.filledCircle;

            var jTxt = plotChannelLayout.Plot.AddText(label, jx.Value + 50, jy.Value + 50);
            jTxt.FontSize = 9; jTxt.Color = color;
        }

        // ══════════════════════════════════════════════════════════════
        // 日志
        // ══════════════════════════════════════════════════════════════

        private void Log(string message)
        {
            if (txtLog.InvokeRequired)
                txtLog.Invoke(() => txtLog.AppendText(message + Environment.NewLine));
            else
                txtLog.AppendText(message + Environment.NewLine);
        }

        // ══════════════════════════════════════════════════════════════
        // ConfigNum helper (mirrors MainForm.Designer pattern)
        // ══════════════════════════════════════════════════════════════

        private static void ConfigNum(NumericUpDown num, decimal value, decimal min, decimal max,
            int decimals, decimal increment)
        {
            num.Minimum       = min;
            num.Maximum       = max;
            num.DecimalPlaces = decimals;
            num.Increment     = increment;
            num.Value         = value;
            num.Dock          = DockStyle.Fill;
        }
    }
}
