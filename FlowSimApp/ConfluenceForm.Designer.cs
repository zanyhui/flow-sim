using System.Drawing;
using System.Windows.Forms;
using ScottPlot;

namespace FlowSim
{
    /// <summary>
    /// ConfluenceForm 的控件布局部分（等价于 WinForms 设计器生成代码）。
    /// </summary>
    partial class ConfluenceForm
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
            this.Text          = "支流汇流仿真";
            this.Size          = new Size(1280, 820);
            this.MinimumSize   = new Size(900, 600);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font          = new Font("Microsoft YaHei", 9f);

            // ── 主拆分条（左侧参数 | 右侧结果）──
            var splitMain = new SplitContainer
            {
                Dock          = DockStyle.Fill,
                Orientation   = System.Windows.Forms.Orientation.Vertical,
                Panel1MinSize = 300,
                Panel2MinSize = 400
            };
            this.Load += (s, e) =>
            {
                try { splitMain.SplitterDistance = 400; }
                catch { }
            };
            this.Controls.Add(splitMain);

            // ══════════════════════════════════════════════
            // 左侧：参数区（可滚动）
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

            // ── 辅助函数 ──
            static TableLayoutPanel MakeTable(int rows, int cols)
            {
                var t = new TableLayoutPanel
                {
                    ColumnCount  = cols,
                    RowCount     = rows,
                    AutoSize     = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Dock         = DockStyle.Fill
                };
                for (int r = 0; r < rows; r++)
                    t.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                return t;
            }

            static Panel MakeCsvRow(Button btn, string btnText, Label lbl, EventHandler clickHandler)
            {
                var row = new Panel { Height = 30, Width = 340 };
                btn.Text     = btnText;
                btn.Location = new Point(0, 3);
                btn.Size     = new Size(90, 24);
                btn.Click   += clickHandler;
                lbl.Text     = "(未选择)";
                lbl.Location = new Point(96, 7);
                lbl.AutoSize = true;
                row.Controls.Add(btn);
                row.Controls.Add(lbl);
                return row;
            }

            int grpWidth = 370;

            // ── GroupBox：支流1 断面数据 ──
            var grpT1 = new GroupBox { Text = "支流1 断面数据", AutoSize = true, Width = grpWidth, Padding = new Padding(5) };
            var flpT1 = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
            flpT1.Controls.Add(MakeCsvRow(btnLoadXsPts1, "加载测点…", lblXsPtsFile1, btnLoadXsPts1_Click));
            flpT1.Controls.Add(MakeCsvRow(btnLoadXsIdx1, "加载索引…", lblXsIdxFile1, btnLoadXsIdx1_Click));
            grpT1.Controls.Add(flpT1);

            // ── GroupBox：支流2 断面数据 ──
            var grpT2 = new GroupBox { Text = "支流2 断面数据", AutoSize = true, Width = grpWidth, Padding = new Padding(5) };
            var flpT2 = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
            flpT2.Controls.Add(MakeCsvRow(btnLoadXsPts2, "加载测点…", lblXsPtsFile2, btnLoadXsPts2_Click));
            flpT2.Controls.Add(MakeCsvRow(btnLoadXsIdx2, "加载索引…", lblXsIdxFile2, btnLoadXsIdx2_Click));
            grpT2.Controls.Add(flpT2);

            // ── GroupBox：干流 断面数据 ──
            var grpMC = new GroupBox { Text = "干流 断面数据", AutoSize = true, Width = grpWidth, Padding = new Padding(5) };
            var flpMC = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true };
            flpMC.Controls.Add(MakeCsvRow(btnLoadXsPts3, "加载测点…", lblXsPtsFile3, btnLoadXsPts3_Click));
            flpMC.Controls.Add(MakeCsvRow(btnLoadXsIdx3, "加载索引…", lblXsIdxFile3, btnLoadXsIdx3_Click));
            grpMC.Controls.Add(flpMC);

            // ── GroupBox：入流参数 ──
            var grpBC = new GroupBox { Text = "入流参数", AutoSize = true, Width = grpWidth, Padding = new Padding(5) };
            var tblBC = MakeTable(8, 2);
            tblBC.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            tblBC.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

            ConfigNum(numInitialFlow, 250, 0, 100000, 0, 100);
            ConfigNum(numPeakFlow1,  1000, 0, 1000000, 0, 100);
            ConfigNum(numRiseTime1,     2, 0.1m, 100, 1, 1m);
            ConfigNum(numPeakFlow2,   500, 0, 1000000, 0, 100);
            ConfigNum(numRiseTime2,     2, 0.1m, 100, 1, 1m);

            void AddRow2(TableLayoutPanel p, string label, Control ctrl)
            {
                p.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left });
                p.Controls.Add(ctrl);
            }

            tblBC.Controls.Add(new Label
            {
                Text = "── 共享 ──",
                Font = new Font("Microsoft YaHei", 9f, System.Drawing.FontStyle.Bold),
                AutoSize = true
            });
            tblBC.Controls.Add(new Label());
            AddRow2(tblBC, "初始流量（m³/s）：", numInitialFlow);
            tblBC.Controls.Add(new Label
            {
                Text = "── 支流1 ──",
                Font = new Font("Microsoft YaHei", 9f, System.Drawing.FontStyle.Bold),
                AutoSize = true
            });
            tblBC.Controls.Add(new Label());
            AddRow2(tblBC, "峰值流量（m³/s）：", numPeakFlow1);
            AddRow2(tblBC, "起涨时间（小时）：", numRiseTime1);
            tblBC.Controls.Add(new Label
            {
                Text = "── 支流2 ──",
                Font = new Font("Microsoft YaHei", 9f, System.Drawing.FontStyle.Bold),
                AutoSize = true
            });
            tblBC.Controls.Add(new Label());
            AddRow2(tblBC, "峰值流量（m³/s）：", numPeakFlow2);
            AddRow2(tblBC, "起涨时间（小时）：", numRiseTime2);
            grpBC.Controls.Add(tblBC);

            // ── GroupBox：下游边界 ──
            var grpDS = new GroupBox { Text = "下游边界", AutoSize = true, Width = grpWidth, Padding = new Padding(5) };
            var tblDS = MakeTable(2, 2);
            tblDS.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            tblDS.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
            cmbDsBcType.Items.AddRange(new[] { "正常水深", "固定水深" });
            cmbDsBcType.SelectedIndex  = 0;
            cmbDsBcType.DropDownStyle  = ComboBoxStyle.DropDownList;
            cmbDsBcType.Dock           = DockStyle.Fill;
            ConfigNum(numDsDepth, 3, 0.01m, 1000, 2, 1m);
            AddRow2(tblDS, "下游边界类型：",      cmbDsBcType);
            AddRow2(tblDS, "初始/固定水深（m）：", numDsDepth);
            grpDS.Controls.Add(tblDS);

            // ── GroupBox：求解器设置 ──
            var grpSolver = new GroupBox { Text = "求解器设置", AutoSize = true, Width = grpWidth, Padding = new Padding(5) };
            var tblSolver = MakeTable(7, 2);
            tblSolver.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
            tblSolver.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));

            cmbSolverMethod.Items.AddRange(new[] { "Preissmann", "Lax-Friedrichs" });
            cmbSolverMethod.SelectedIndex  = 0;
            cmbSolverMethod.DropDownStyle  = ComboBoxStyle.DropDownList;
            cmbSolverMethod.Dock           = DockStyle.Fill;
            cmbSolverMethod.SelectedIndexChanged += (s, e) =>
            {
                bool isP = cmbSolverMethod.SelectedIndex == 0;
                numTheta.Enabled     = isP;
                numTolerance.Enabled = isP;
                numMaxIter.Enabled   = isP;
            };

            ConfigNum(numTimeStep,    60,     1,       3600,   0,    60);
            ConfigNum(numSpatialStep, 500,    10,      10000,  0,    500);
            ConfigNum(numSimTime,     24,     1,       720,    0,    24);
            ConfigNum(numTheta,       0.6m,   0.5m,    1.0m,   2,    0.05m);
            numTolerance.Minimum       = 1e-10m;
            numTolerance.Maximum       = 1m;
            numTolerance.DecimalPlaces = 8;
            numTolerance.Increment     = 0.0001m;
            numTolerance.Value         = 0.0001m;
            numTolerance.Dock          = DockStyle.Fill;
            ConfigNum(numMaxIter, 100, 1, 10000, 0, 10);

            AddRow2(tblSolver, "求解方法：",              cmbSolverMethod);
            AddRow2(tblSolver, "时间步长（s）：",         numTimeStep);
            AddRow2(tblSolver, "空间步长（m）：",         numSpatialStep);
            AddRow2(tblSolver, "模拟时长（小时）：",      numSimTime);
            AddRow2(tblSolver, "θ（Preissmann）：",       numTheta);
            AddRow2(tblSolver, "容差（Preissmann）：",    numTolerance);
            AddRow2(tblSolver, "最大迭代（Preissmann）：",numMaxIter);
            grpSolver.Controls.Add(tblSolver);

            // ── 运行/保存按钮 ──
            var pnlButtons = new Panel { Height = 40, Width = grpWidth };
            btnRun.Text      = "▶ 运行仿真";
            btnRun.Size      = new Size(150, 34);
            btnRun.Location  = new Point(0, 3);
            btnRun.BackColor = Color.FromArgb(0, 122, 204);
            btnRun.ForeColor = Color.White;
            btnRun.FlatStyle = FlatStyle.Flat;
            btnRun.Click    += btnRun_Click;
            btnRun.Enabled   = false;

            btnSave.Text      = "💾 保存结果";
            btnSave.Size      = new Size(150, 34);
            btnSave.Location  = new Point(160, 3);
            btnSave.FlatStyle = FlatStyle.Flat;
            btnSave.Click    += btnSave_Click;
            btnSave.Enabled   = false;

            pnlButtons.Controls.Add(btnRun);
            pnlButtons.Controls.Add(btnSave);

            // ── 日志文本框 ──
            txtLog.Multiline  = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly   = true;
            txtLog.Width      = grpWidth;
            txtLog.Height     = 120;
            txtLog.BackColor  = Color.Black;
            txtLog.ForeColor  = Color.Lime;
            txtLog.Font       = new Font("Consolas", 8);

            // 将所有左侧控件加入 leftFlow（从上到下）
            leftFlow.Controls.Add(grpT1);
            leftFlow.Controls.Add(grpT2);
            leftFlow.Controls.Add(grpMC);
            leftFlow.Controls.Add(grpBC);
            leftFlow.Controls.Add(grpDS);
            leftFlow.Controls.Add(grpSolver);
            leftFlow.Controls.Add(pnlButtons);
            leftFlow.Controls.Add(txtLog);

            // ══════════════════════════════════════════════
            // 右侧：结果 TabControl
            // ══════════════════════════════════════════════
            var tabResults = new TabControl { Dock = DockStyle.Fill };
            var tabPageResults  = new TabPage("干流结果");
            var tabPageJunction = new TabPage("汇流水文");
            var tabPageTrib1    = new TabPage("支流1");
            var tabPageTrib2    = new TabPage("支流2");
            var tabPageCharts   = new TabPage("图表");
            var tabPageData     = new TabPage("数据");
            var tabPageLayout   = new TabPage("平面图");
            tabResults.TabPages.Add(tabPageResults);
            tabResults.TabPages.Add(tabPageJunction);
            tabResults.TabPages.Add(tabPageTrib1);
            tabResults.TabPages.Add(tabPageTrib2);
            tabResults.TabPages.Add(tabPageCharts);
            tabResults.TabPages.Add(tabPageData);
            tabResults.TabPages.Add(tabPageLayout);
            splitMain.Panel2.Controls.Add(tabResults);

            // ── Tab "结果" ──
            var splitResultsH = new SplitContainer
            {
                Dock              = DockStyle.Fill,
                Orientation       = System.Windows.Forms.Orientation.Horizontal,
                SplitterDistance  = 350
            };

            var splitResultsV = new SplitContainer
            {
                Dock        = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Vertical
            };
            plotFlow.Dock    = DockStyle.Fill;
            plotProfile.Dock = DockStyle.Fill;
            splitResultsV.Panel1.Controls.Add(plotFlow);
            splitResultsV.Panel2.Controls.Add(plotProfile);
            splitResultsH.Panel1.Controls.Add(splitResultsV);

            gridSummary.Dock              = DockStyle.Fill;
            gridSummary.ColumnCount       = 2;
            gridSummary.Columns[0].Name   = "参数";
            gridSummary.Columns[1].Name   = "数值";
            gridSummary.Columns[0].Width  = 250;
            gridSummary.Columns[1].Width  = 150;
            gridSummary.AllowUserToAddRows = false;
            gridSummary.ReadOnly           = true;
            gridSummary.AutoSizeRowsMode   = DataGridViewAutoSizeRowsMode.AllCells;

            gridCfl.Dock              = DockStyle.Fill;
            gridCfl.ColumnCount       = 3;
            gridCfl.Columns[0].Name   = "时步";
            gridCfl.Columns[1].Name   = "模拟时刻（h）";
            gridCfl.Columns[2].Name   = "最大 CFL";
            gridCfl.Columns[0].Width  = 80;
            gridCfl.Columns[1].Width  = 120;
            gridCfl.Columns[2].Width  = 120;
            gridCfl.AllowUserToAddRows = false;
            gridCfl.ReadOnly           = true;
            gridCfl.AutoSizeRowsMode   = DataGridViewAutoSizeRowsMode.AllCells;

            var tabBottom = new TabControl { Dock = DockStyle.Fill };
            var tabSummary = new TabPage("统计汇总");
            var tabCfl     = new TabPage("CFL 条件");
            tabSummary.Controls.Add(gridSummary);
            tabCfl.Controls.Add(gridCfl);
            tabBottom.TabPages.Add(tabSummary);
            tabBottom.TabPages.Add(tabCfl);
            splitResultsH.Panel2.Controls.Add(tabBottom);

            tabPageResults.Controls.Add(splitResultsH);

            // ── Tab "汇流水文" ──
            plotJunction.Dock = DockStyle.Fill;
            tabPageJunction.Controls.Add(plotJunction);

            // ── Tab "支流1" ──
            var splitTrib1 = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                Orientation      = System.Windows.Forms.Orientation.Vertical,
                SplitterDistance = 400
            };
            plotTrib1Flow.Dock    = DockStyle.Fill;
            plotTrib1Profile.Dock = DockStyle.Fill;
            splitTrib1.Panel1.Controls.Add(plotTrib1Flow);
            splitTrib1.Panel2.Controls.Add(plotTrib1Profile);
            tabPageTrib1.Controls.Add(splitTrib1);

            // ── Tab "支流2" ──
            var splitTrib2 = new SplitContainer
            {
                Dock             = DockStyle.Fill,
                Orientation      = System.Windows.Forms.Orientation.Vertical,
                SplitterDistance = 400
            };
            plotTrib2Flow.Dock    = DockStyle.Fill;
            plotTrib2Profile.Dock = DockStyle.Fill;
            splitTrib2.Panel1.Controls.Add(plotTrib2Flow);
            splitTrib2.Panel2.Controls.Add(plotTrib2Profile);
            tabPageTrib2.Controls.Add(splitTrib2);

            // ── Tab "图表" ──
            var splitCharts = new SplitContainer
            {
                Dock              = DockStyle.Fill,
                Orientation       = System.Windows.Forms.Orientation.Horizontal,
                SplitterDistance  = 320
            };

            // 纵断面（上半）
            var pnlLongCtrl = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(5, 6, 5, 0) };
            var lblLongTitle = new Label { Text = "纵断面水位", Location = new Point(5, 12), AutoSize = true, Font = new Font("Microsoft YaHei", 9f, System.Drawing.FontStyle.Bold) };
            var lblLongSlider = new Label { Text = "时间：", Location = new Point(110, 14), AutoSize = true };
            trkLongTime.Minimum = 0; trkLongTime.Maximum = 100; trkLongTime.Value = 0;
            trkLongTime.TickFrequency = 10; trkLongTime.AutoSize = false;
            trkLongTime.Location = new Point(158, 4); trkLongTime.Size = new Size(380, 42);
            trkLongTime.Enabled = false; trkLongTime.Scroll += trkLongTime_Scroll;
            lblLongTime.Text = "—"; lblLongTime.Location = new Point(550, 14); lblLongTime.AutoSize = true;
            pnlLongCtrl.Controls.Add(lblLongTitle);
            pnlLongCtrl.Controls.Add(lblLongSlider);
            pnlLongCtrl.Controls.Add(trkLongTime);
            pnlLongCtrl.Controls.Add(lblLongTime);
            plotLongProfile.Dock = DockStyle.Fill;
            splitCharts.Panel1.Controls.Add(plotLongProfile);
            splitCharts.Panel1.Controls.Add(pnlLongCtrl);

            // 横断面（下半）
            var pnlXsCtrl = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(5, 6, 5, 0) };
            var lblXsTitle = new Label { Text = "断面形状", Location = new Point(5, 12), AutoSize = true, Font = new Font("Microsoft YaHei", 9f, System.Drawing.FontStyle.Bold) };
            var lblXsNodeLabel = new Label { Text = "节点：", Location = new Point(95, 14), AutoSize = true };
            cmbXsNode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbXsNode.Location = new Point(140, 10); cmbXsNode.Size = new Size(180, 24);
            cmbXsNode.Enabled = false; cmbXsNode.SelectedIndexChanged += cmbXsNode_SelectedIndexChanged;
            var lblXsSlider = new Label { Text = "时间：", Location = new Point(332, 14), AutoSize = true };
            trkXsTime.Minimum = 0; trkXsTime.Maximum = 100; trkXsTime.Value = 0;
            trkXsTime.TickFrequency = 10; trkXsTime.AutoSize = false;
            trkXsTime.Location = new Point(378, 4); trkXsTime.Size = new Size(320, 42);
            trkXsTime.Enabled = false; trkXsTime.Scroll += trkXsTime_Scroll;
            lblXsTime.Text = "—"; lblXsTime.Location = new Point(710, 14); lblXsTime.AutoSize = true;
            pnlXsCtrl.Controls.Add(lblXsTitle);
            pnlXsCtrl.Controls.Add(lblXsNodeLabel);
            pnlXsCtrl.Controls.Add(cmbXsNode);
            pnlXsCtrl.Controls.Add(lblXsSlider);
            pnlXsCtrl.Controls.Add(trkXsTime);
            pnlXsCtrl.Controls.Add(lblXsTime);
            plotXsShape.Dock = DockStyle.Fill;
            splitCharts.Panel2.Controls.Add(plotXsShape);
            splitCharts.Panel2.Controls.Add(pnlXsCtrl);

            tabPageCharts.Controls.Add(splitCharts);

            // ── Tab "数据" ──
            var pnlDataCtrl = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8, 8, 8, 0) };
            var lblDataTitle = new Label { Text = "节点水动力数据", Location = new Point(8, 14), AutoSize = true, Font = new Font("Microsoft YaHei", 9f, System.Drawing.FontStyle.Bold) };
            var lblDataSlider = new Label { Text = "时间：", Location = new Point(170, 16), AutoSize = true };
            trkDataTime.Minimum = 0; trkDataTime.Maximum = 100; trkDataTime.Value = 0;
            trkDataTime.TickFrequency = 10; trkDataTime.AutoSize = false;
            trkDataTime.Location = new Point(218, 6); trkDataTime.Size = new Size(430, 38);
            trkDataTime.Enabled = false; trkDataTime.Scroll += trkDataTime_Scroll;
            lblDataTime.Text = "—"; lblDataTime.Location = new Point(658, 16); lblDataTime.AutoSize = true;
            pnlDataCtrl.Controls.Add(lblDataTitle);
            pnlDataCtrl.Controls.Add(lblDataSlider);
            pnlDataCtrl.Controls.Add(trkDataTime);
            pnlDataCtrl.Controls.Add(lblDataTime);

            gridData.Dock                = DockStyle.Fill;
            gridData.AllowUserToAddRows  = false;
            gridData.ReadOnly            = true;
            gridData.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridData.SelectionMode       = DataGridViewSelectionMode.FullRowSelect;
            gridData.RowHeadersVisible   = false;
            gridData.ColumnCount         = 9;
            gridData.Columns[0].Name = "节点";
            gridData.Columns[1].Name = "桩号 (km)";
            gridData.Columns[2].Name = "流量 (m³/s)";
            gridData.Columns[3].Name = "水位 (m)";
            gridData.Columns[4].Name = "水深 (m)";
            gridData.Columns[5].Name = "过水面积 (m²)";
            gridData.Columns[6].Name = "水面宽 (m)";
            gridData.Columns[7].Name = "流速 (m/s)";
            gridData.Columns[8].Name = "Fr";
            gridData.Columns[0].FillWeight = 50;
            gridData.Columns[1].FillWeight = 80;
            for (int col = 2; col < 9; col++) gridData.Columns[col].FillWeight = 100;
            gridData.AlternatingRowsDefaultCellStyle.BackColor = Color.AliceBlue;

            tabPageData.Controls.Add(gridData);
            tabPageData.Controls.Add(pnlDataCtrl);

            // ── Tab "平面图" ──
            plotChannelLayout.Dock = DockStyle.Fill;
            tabPageLayout.Controls.Add(plotChannelLayout);

            ResumeLayout(false);
        }

        // ── 控件字段声明 ──

        // 支流1 CSV 按钮 + 标签
        private Button btnLoadXsPts1 = new Button();
        private Label  lblXsPtsFile1 = new Label();
        private Button btnLoadXsIdx1 = new Button();
        private Label  lblXsIdxFile1 = new Label();
        // 支流2
        private Button btnLoadXsPts2 = new Button();
        private Label  lblXsPtsFile2 = new Label();
        private Button btnLoadXsIdx2 = new Button();
        private Label  lblXsIdxFile2 = new Label();
        // 干流
        private Button btnLoadXsPts3 = new Button();
        private Label  lblXsPtsFile3 = new Label();
        private Button btnLoadXsIdx3 = new Button();
        private Label  lblXsIdxFile3 = new Label();

        // 入流参数
        private NumericUpDown numInitialFlow = new NumericUpDown();
        private NumericUpDown numPeakFlow1   = new NumericUpDown();
        private NumericUpDown numRiseTime1   = new NumericUpDown();
        private NumericUpDown numPeakFlow2   = new NumericUpDown();
        private NumericUpDown numRiseTime2   = new NumericUpDown();

        // 下游边界
        private ComboBox      cmbDsBcType = new ComboBox();
        private NumericUpDown numDsDepth  = new NumericUpDown();

        // 求解器
        private ComboBox      cmbSolverMethod = new ComboBox();
        private NumericUpDown numTimeStep     = new NumericUpDown();
        private NumericUpDown numSpatialStep  = new NumericUpDown();
        private NumericUpDown numSimTime      = new NumericUpDown();
        private NumericUpDown numTheta        = new NumericUpDown();
        private NumericUpDown numTolerance    = new NumericUpDown();
        private NumericUpDown numMaxIter      = new NumericUpDown();

        // 按钮 + 日志
        private Button  btnRun  = new Button();
        private Button  btnSave = new Button();
        private TextBox txtLog  = new TextBox();

        // 结果图表
        private FormsPlot    plotFlow    = new FormsPlot();
        private FormsPlot    plotProfile = new FormsPlot();
        private DataGridView gridSummary = new DataGridView();
        private DataGridView gridCfl     = new DataGridView();

        // 图表 tab
        private FormsPlot plotLongProfile = new FormsPlot();
        private FormsPlot plotXsShape     = new FormsPlot();
        private TrackBar  trkLongTime     = new TrackBar();
        private TrackBar  trkXsTime       = new TrackBar();
        private Label     lblLongTime     = new Label();
        private Label     lblXsTime       = new Label();
        private ComboBox  cmbXsNode       = new ComboBox();

        // 数据 tab
        private TrackBar     trkDataTime = new TrackBar();
        private Label        lblDataTime = new Label();
        private DataGridView gridData    = new DataGridView();

        // 平面图 tab
        private FormsPlot plotChannelLayout = new FormsPlot();

        // 汇流水文 tab
        private FormsPlot plotJunction = new FormsPlot();

        // 支流1 / 支流2 结果 tab
        private FormsPlot plotTrib1Flow    = new FormsPlot();
        private FormsPlot plotTrib1Profile = new FormsPlot();
        private FormsPlot plotTrib2Flow    = new FormsPlot();
        private FormsPlot plotTrib2Profile = new FormsPlot();

        #endregion
    }
}
