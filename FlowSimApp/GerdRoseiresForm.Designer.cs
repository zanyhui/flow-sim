using System;
using System.Drawing;
using System.Windows.Forms;
using ScottPlot;

namespace FlowSim
{
    partial class GerdRoseiresForm
    {
        private System.ComponentModel.IContainer? components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components != null) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            SuspendLayout();

            // ── 窗体基本属性 ──
            this.Text          = "GERD-Roseires 水库联合调度仿真";
            this.Size          = new Size(1280, 820);
            this.MinimumSize   = new Size(900, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font          = new Font("Microsoft YaHei", 9f);

            // ── 主拆分条（左侧参数 | 右侧结果）──
            var splitMain = new SplitContainer
            {
                Dock         = DockStyle.Fill,
                Orientation  = System.Windows.Forms.Orientation.Vertical,
                SplitterDistance = 380,
                Panel1MinSize    = 300,
                Panel2MinSize    = 400
            };
            this.Controls.Add(splitMain);

            // ══════════════════════════════════════════════
            // 左侧：参数 + 文件路径 + 运行按钮
            // ══════════════════════════════════════════════
            var leftScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            splitMain.Panel1.Controls.Add(leftScroll);

            var leftFlow = new FlowLayoutPanel
            {
                Dock          = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents  = false,
                AutoSize      = true,
                AutoSizeMode  = AutoSizeMode.GrowAndShrink,
                Padding       = new Padding(6),
            };
            leftScroll.Controls.Add(leftFlow);

            // ── 数据文件 GroupBox ──
            var grpFiles = new GroupBox { Text = "数据文件", AutoSize = true, Width = 360, Padding = new Padding(5) };
            var tblFiles = MakeTable(6, 3);
            tblFiles.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
            tblFiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            tblFiles.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70));

            txtXsPath       = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            txtInflowPath   = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            txtVolCurvePath = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            txtSpillwayPath = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            txtSluicePath    = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };
            txtCoordsPath   = new TextBox { Dock = DockStyle.Fill, ReadOnly = true };

            btnBrowseXs       = new Button { Text = "浏览…", Dock = DockStyle.Fill };
            btnBrowseInflow   = new Button { Text = "浏览…", Dock = DockStyle.Fill };
            btnBrowseVolCurve = new Button { Text = "浏览…", Dock = DockStyle.Fill };
            btnBrowseSpillway = new Button { Text = "浏览…", Dock = DockStyle.Fill };
            btnBrowseSluice   = new Button { Text = "浏览…", Dock = DockStyle.Fill };
            btnBrowseCoords   = new Button { Text = "浏览…", Dock = DockStyle.Fill };

            btnBrowseXs.Click       += btnBrowseXs_Click;
            btnBrowseInflow.Click   += btnBrowseInflow_Click;
            btnBrowseVolCurve.Click += btnBrowseVolCurve_Click;
            btnBrowseSpillway.Click += btnBrowseSpillway_Click;
            btnBrowseSluice.Click   += btnBrowseSluice_Click;
            btnBrowseCoords.Click   += btnBrowseCoords_Click;

            AddFileRow(tblFiles, 0, "复式断面 CSV",   txtXsPath,       btnBrowseXs);
            AddFileRow(tblFiles, 1, "入库过程线 CSV", txtInflowPath,   btnBrowseInflow);
            AddFileRow(tblFiles, 2, "GERD V-Z CSV",   txtVolCurvePath, btnBrowseVolCurve);
            AddFileRow(tblFiles, 3, "溢洪道流量 CSV", txtSpillwayPath, btnBrowseSpillway);
            AddFileRow(tblFiles, 4, "深孔泄槽 CSV",   txtSluicePath,    btnBrowseSluice);
            AddFileRow(tblFiles, 5, "中心线坐标 CSV", txtCoordsPath,   btnBrowseCoords);

            grpFiles.Controls.Add(tblFiles);
            leftFlow.Controls.Add(grpFiles);

            // ── 仿真参数 GroupBox ──
            var grpSim = new GroupBox { Text = "仿真参数", AutoSize = true, Width = 360, Padding = new Padding(5) };
            var tblSim = MakeTable(3, 2);

            numGerdLevel     = MakeNum(637, 550, 660, 1, 1, 1m);
            numRoseiresLevel = MakeNum(487, 466.7m, 492, 1, 1, 0.1m);
            numSimTime       = MakeNum(384, 1, 10000, 0, 1, 24m);  // 小时

            AddRow2(tblSim, "GERD 初始水位 (m)：",      numGerdLevel);
            AddRow2(tblSim, "Roseires 初始水位 (m)：",  numRoseiresLevel);
            AddRow2(tblSim, "模拟时长 (h，0=自动)：",   numSimTime);

            grpSim.Controls.Add(tblSim);
            leftFlow.Controls.Add(grpSim);

            // ── 求解器参数 GroupBox ──
            var grpSolver = new GroupBox { Text = "求解器参数", AutoSize = true, Width = 360, Padding = new Padding(5) };
            var tblSolver = MakeTable(4, 2);

            numTheta      = MakeNum(0.6m, 0.5m, 1, 2, 1, 0.05m);
            numTimeStep   = MakeNum(3600, 60, 86400, 0, 1, 600m);
            numSpatialStep= MakeNum(1000, 100, 50000, 0, 1, 500m);
            numTolerance  = MakeNum(1e-6m, 1e-10m, 1e-2m, 8, 1, 1e-7m);

            AddRow2(tblSolver, "θ（Preissmann 权重）：", numTheta);
            AddRow2(tblSolver, "时间步长 dt（s）：",      numTimeStep);
            AddRow2(tblSolver, "空间步长 dx（m）：",      numSpatialStep);
            AddRow2(tblSolver, "迭代容差：",              numTolerance);

            grpSolver.Controls.Add(tblSolver);
            leftFlow.Controls.Add(grpSolver);

            // ── 情景参数 GroupBox ──
            var grpScene = new GroupBox { Text = "情景参数", AutoSize = true, Width = 360, Padding = new Padding(5) };
            var tblScene = MakeTable(3, 2);

            chkWithGerd      = new CheckBox { Text = "包含 GERD 调蓄效应", Checked = true, AutoSize = true };
            numJamSpillways  = MakeNum(0, 0, 7, 0, 1, 1m);
            numJamSluices    = MakeNum(0, 0, 5, 0, 1, 1m);

            var pnlWithGerd = new Panel { Dock = DockStyle.Fill };
            pnlWithGerd.Controls.Add(chkWithGerd);
            chkWithGerd.Location = new Point(2, 3);

            tblScene.Controls.Add(new Label { Text = "GERD 情景：", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
            tblScene.Controls.Add(pnlWithGerd, 1, 0);
            AddRow2(tblScene, "卡闸溢洪道数：", numJamSpillways);
            AddRow2(tblScene, "卡闸泄槽数：",   numJamSluices);

            grpScene.Controls.Add(tblScene);
            leftFlow.Controls.Add(grpScene);

            // ── 运行按钮 ──
            btnRun = new Button
            {
                Text   = "▶  运行仿真",
                Width  = 360,
                Height = 36,
                BackColor = Color.FromArgb(0, 120, 215),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Microsoft YaHei", 10f, FontStyle.Bold)
            };
            btnRun.Click += btnRun_Click;
            leftFlow.Controls.Add(btnRun);

            // ── 日志文本框 ──
            txtLog = new TextBox
            {
                Multiline   = true,
                ScrollBars  = ScrollBars.Vertical,
                ReadOnly    = true,
                Font        = new Font("Consolas", 8.5f),
                Dock        = DockStyle.Bottom,
                Height      = 160,
                BackColor   = Color.FromArgb(30, 30, 30),
                ForeColor   = Color.LightGray
            };
            splitMain.Panel1.Controls.Add(txtLog);

            // ══════════════════════════════════════════════
            // 右侧：图表选项卡
            // ══════════════════════════════════════════════
            var tabResults = new TabControl { Dock = DockStyle.Fill };
            splitMain.Panel2.Controls.Add(tabResults);

            var tabFlow    = new TabPage("流量历时曲线");
            var tabProfile = new TabPage("纵向水面线");

            plotFlow    = new FormsPlot { Dock = DockStyle.Fill };
            plotProfile = new FormsPlot { Dock = DockStyle.Fill };

            tabFlow.Controls.Add(plotFlow);
            tabProfile.Controls.Add(plotProfile);

            tabResults.TabPages.Add(tabFlow);
            tabResults.TabPages.Add(tabProfile);

            // 初始空白提示
            plotFlow.Plot.Title("请先运行仿真");
            plotProfile.Plot.Title("请先运行仿真");
            plotFlow.Refresh();
            plotProfile.Refresh();

            ResumeLayout();
        }

        // ── 布局辅助方法 ──
        private static TableLayoutPanel MakeTable(int rows, int cols)
        {
            var t = new TableLayoutPanel
            {
                Dock     = DockStyle.Fill,
                RowCount = rows,
                ColumnCount = cols,
                AutoSize = true
            };
            for (int i = 0; i < cols; i++)
                t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            for (int i = 0; i < rows; i++)
                t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            return t;
        }

        private static NumericUpDown MakeNum(decimal val, decimal min, decimal max, int decimals, int width, decimal increment)
        {
            return new NumericUpDown
            {
                Minimum       = min,
                Maximum       = max,
                DecimalPlaces = decimals,
                Increment     = increment,
                Value         = val,
                Dock          = DockStyle.Fill
            };
        }

        private static void AddRow2(TableLayoutPanel t, string label, Control ctrl)
        {
            int row = t.RowCount;
            t.RowCount++;
            t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            t.Controls.Add(ctrl, 1, row);
        }

        private static void AddFileRow(TableLayoutPanel t, int row,
                                        string label, TextBox txt, Button btn)
        {
            t.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            t.Controls.Add(txt, 1, row);
            t.Controls.Add(btn, 2, row);
        }

        #endregion

        // ── 控件字段声明 ──
        private TextBox   txtXsPath        = null!;
        private TextBox   txtInflowPath    = null!;
        private TextBox   txtVolCurvePath  = null!;
        private TextBox   txtSpillwayPath  = null!;
        private TextBox   txtSluicePath     = null!;
        private TextBox   txtCoordsPath    = null!;

        private Button    btnBrowseXs       = null!;
        private Button    btnBrowseInflow   = null!;
        private Button    btnBrowseVolCurve = null!;
        private Button    btnBrowseSpillway = null!;
        private Button    btnBrowseSluice   = null!;
        private Button    btnBrowseCoords   = null!;

        private NumericUpDown numGerdLevel      = null!;
        private NumericUpDown numRoseiresLevel  = null!;
        private NumericUpDown numSimTime        = null!;
        private NumericUpDown numTheta          = null!;
        private NumericUpDown numTimeStep       = null!;
        private NumericUpDown numSpatialStep    = null!;
        private NumericUpDown numTolerance      = null!;
        private NumericUpDown numJamSpillways   = null!;
        private NumericUpDown numJamSluices     = null!;

        private CheckBox  chkWithGerd    = null!;
        private Button    btnRun         = null!;
        private TextBox   txtLog         = null!;
        private FormsPlot plotFlow       = null!;
        private FormsPlot plotProfile    = null!;
    }
}
