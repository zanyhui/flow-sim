using System.Windows.Forms;
using ScottPlot;

namespace FlowSim
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            this.tabControl = new TabControl();
            this.tabChannel = new TabPage("河道设置");
            this.tabBoundary = new TabPage("边界条件");
            this.tabSolver = new TabPage("求解器设置");
            this.tabResults = new TabPage("结果");

            // ---- Channel Setup controls ----
            this.numLength      = new NumericUpDown();
            this.numWidth       = new NumericUpDown();
            this.numRoughness   = new NumericUpDown();
            this.numInitialFlow = new NumericUpDown();
            this.numUsBedLevel  = new NumericUpDown();
            this.numDsBedLevel  = new NumericUpDown();

            // ---- Boundary controls ----
            this.cmbUsBcType  = new ComboBox();
            this.cmbDsBcType  = new ComboBox();
            this.numPeakFlow  = new NumericUpDown();
            this.numRiseTime  = new NumericUpDown();
            this.numDsDepth   = new NumericUpDown();

            // ---- Solver controls ----
            this.cmbSolverMethod = new ComboBox();
            this.numTimeStep     = new NumericUpDown();
            this.numSpatialStep  = new NumericUpDown();
            this.numSimTime      = new NumericUpDown();
            this.numTheta        = new NumericUpDown();

            // ---- Results controls ----
            this.plotFlow    = new FormsPlot();
            this.plotProfile = new FormsPlot();
            this.gridSummary = new DataGridView();

            // ---- Buttons / log ----
            this.btnRun  = new Button();
            this.btnSave = new Button();
            this.txtLog  = new TextBox();

            this.SuspendLayout();

            // ===== TAB CONTROL =====
            tabControl.Dock = DockStyle.Fill;
            tabControl.TabPages.Add(tabChannel);
            tabControl.TabPages.Add(tabBoundary);
            tabControl.TabPages.Add(tabSolver);
            tabControl.TabPages.Add(tabResults);

            // ===== CHANNEL SETUP TAB =====
            var pnlChannel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new System.Windows.Forms.Padding(10)
            };
            pnlChannel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlChannel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            void AddRow(TableLayoutPanel p, string label, NumericUpDown num)
            {
                p.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true });
                p.Controls.Add(num);
            }

            ConfigNum(numLength, 20000, 100, 100000, 0, 20000);
            ConfigNum(numWidth, 250, 1, 10000, 0, 250);
            ConfigNum(numRoughness, 0.027m, 0.001m, 0.5m, 3, 0.027m);
            ConfigNum(numInitialFlow, 250, 0, 100000, 0, 250);
            ConfigNum(numUsBedLevel, 10, -1000, 10000, 1, 10);
            ConfigNum(numDsBedLevel, 8, -1000, 10000, 1, 8);

            AddRow(pnlChannel, "河道长度（m）：", numLength);
            AddRow(pnlChannel, "河道宽度（m）：", numWidth);
            AddRow(pnlChannel, "曼宁糙率 n：", numRoughness);
            AddRow(pnlChannel, "初始流量（m³/s）：", numInitialFlow);
            AddRow(pnlChannel, "上游床底高程（m）：", numUsBedLevel);
            AddRow(pnlChannel, "下游床底高程（m）：", numDsBedLevel);

            tabChannel.Controls.Add(pnlChannel);

            // ===== BOUNDARY CONDITIONS TAB =====
            var pnlBoundary = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10,
                Padding = new System.Windows.Forms.Padding(10)
            };
            pnlBoundary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlBoundary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            cmbUsBcType.Items.AddRange(new[] { "流量过程线", "正常水深" });
            cmbUsBcType.SelectedIndex = 0; cmbUsBcType.DropDownStyle = ComboBoxStyle.DropDownList;

            cmbDsBcType.Items.AddRange(new[] { "正常水深", "固定水深" });
            cmbDsBcType.SelectedIndex = 0; cmbDsBcType.DropDownStyle = ComboBoxStyle.DropDownList;

            ConfigNum(numPeakFlow, 1000, 0, 1000000, 0, 1000);
            ConfigNum(numRiseTime, 2, 0.1m, 100, 1, 2);
            ConfigNum(numDsDepth, 3, 0.01m, 1000, 2, 3);

            void AddRow2(TableLayoutPanel p, string label, Control ctrl)
            {
                p.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true });
                p.Controls.Add(ctrl);
            }

            pnlBoundary.Controls.Add(new Label { Text = "─── 上游 ───", Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold) });
            pnlBoundary.Controls.Add(new Label());
            AddRow2(pnlBoundary, "上游边界条件类型：", cmbUsBcType);
            AddRow2(pnlBoundary, "峰值流量（m³/s）：", numPeakFlow);
            AddRow2(pnlBoundary, "起涨时间（小时）：", numRiseTime);
            pnlBoundary.Controls.Add(new Label { Text = "─── 下游 ───", Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold) });
            pnlBoundary.Controls.Add(new Label());
            AddRow2(pnlBoundary, "下游边界条件类型：", cmbDsBcType);
            AddRow2(pnlBoundary, "初始/固定水深（m）：", numDsDepth);

            tabBoundary.Controls.Add(pnlBoundary);

            // ===== SOLVER SETTINGS TAB =====
            var pnlSolver = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new System.Windows.Forms.Padding(10)
            };
            pnlSolver.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlSolver.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            cmbSolverMethod.Items.AddRange(new[] { "Preissmann", "Lax-Friedrichs" });
            cmbSolverMethod.SelectedIndex = 0; cmbSolverMethod.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSolverMethod.SelectedIndexChanged += (s, e) => numTheta.Enabled = cmbSolverMethod.SelectedIndex == 0;

            ConfigNum(numTimeStep, 60, 1, 3600, 0, 60);
            ConfigNum(numSpatialStep, 500, 10, 10000, 0, 500);
            ConfigNum(numSimTime, 24, 1, 720, 0, 24);
            ConfigNum(numTheta, 0.6m, 0.5m, 1.0m, 2, 0.6m);

            AddRow(pnlSolver, "时间步长（s）：", numTimeStep);
            pnlSolver.Controls.Add(new Label { Text = "求解方法：", AutoSize = true });
            pnlSolver.Controls.Add(cmbSolverMethod);
            AddRow(pnlSolver, "空间步长（m）：", numSpatialStep);
            AddRow(pnlSolver, "模拟时长（小时）：", numSimTime);
            AddRow(pnlSolver, "θ 参数（Preissmann）：", numTheta);

            tabSolver.Controls.Add(pnlSolver);

            // ===== RESULTS TAB =====
            var splitResults = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Horizontal,
                SplitterDistance = 350
            };

            var splitPlots = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Vertical
            };

            plotFlow.Dock = DockStyle.Fill;
            plotProfile.Dock = DockStyle.Fill;
            splitPlots.Panel1.Controls.Add(plotFlow);
            splitPlots.Panel2.Controls.Add(plotProfile);

            gridSummary.Dock = DockStyle.Fill;
            gridSummary.ColumnCount = 2;
            gridSummary.Columns[0].Name = "参数";
            gridSummary.Columns[1].Name = "数值";
            gridSummary.Columns[0].Width = 250;
            gridSummary.Columns[1].Width = 150;
            gridSummary.AllowUserToAddRows = false;
            gridSummary.ReadOnly = true;
            gridSummary.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

            splitResults.Panel1.Controls.Add(splitPlots);
            splitResults.Panel2.Controls.Add(gridSummary);

            tabResults.Controls.Add(splitResults);

            // ===== BUTTONS & LOG =====
            btnRun.Text = "▶ 运行仿真";
            btnRun.Size = new System.Drawing.Size(150, 35);
            btnRun.Location = new System.Drawing.Point(10, 5);
            btnRun.Click += btnRun_Click;
            btnRun.BackColor = System.Drawing.Color.FromArgb(0, 122, 204);
            btnRun.ForeColor = System.Drawing.Color.White;
            btnRun.FlatStyle = FlatStyle.Flat;

            btnSave.Text = "💾 保存结果";
            btnSave.Size = new System.Drawing.Size(150, 35);
            btnSave.Location = new System.Drawing.Point(170, 5);
            btnSave.Click += btnSave_Click;
            btnSave.Enabled = false;
            btnSave.FlatStyle = FlatStyle.Flat;

            txtLog.Multiline = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.ReadOnly = true;
            txtLog.Location = new System.Drawing.Point(10, 45);
            txtLog.Width = 760;
            txtLog.Height = 80;
            txtLog.BackColor = System.Drawing.Color.Black;
            txtLog.ForeColor = System.Drawing.Color.Lime;
            txtLog.Font = new System.Drawing.Font("Consolas", 8);

            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 135 };
            pnlBottom.Controls.Add(btnRun);
            pnlBottom.Controls.Add(btnSave);
            pnlBottom.Controls.Add(txtLog);

            // ===== MAIN FORM =====
            this.Text = "FlowSim – 一维水动力仿真";
            this.Size = new System.Drawing.Size(900, 650);
            this.MinimumSize = new System.Drawing.Size(700, 500);
            this.Controls.Add(tabControl);
            this.Controls.Add(pnlBottom);

            this.ResumeLayout(false);
        }

        private static void ConfigNum(NumericUpDown num, decimal value, decimal min, decimal max, int decimals, decimal increment)
        {
            num.Minimum = min; num.Maximum = max;
            num.DecimalPlaces = decimals; num.Increment = increment;
            num.Value = value; num.Dock = DockStyle.Fill;
        }

        // ---- Controls ----
        private TabControl tabControl;
        private TabPage tabChannel, tabBoundary, tabSolver, tabResults;
        private NumericUpDown numLength, numWidth, numRoughness, numInitialFlow;
        private NumericUpDown numUsBedLevel, numDsBedLevel;
        private ComboBox cmbUsBcType, cmbDsBcType;
        private NumericUpDown numPeakFlow, numRiseTime, numDsDepth;
        private ComboBox cmbSolverMethod;
        private NumericUpDown numTimeStep, numSpatialStep, numSimTime, numTheta;
        private FormsPlot plotFlow, plotProfile;
        private DataGridView gridSummary;
        private Button btnRun, btnSave;
        private TextBox txtLog;

        #endregion
    }
}
