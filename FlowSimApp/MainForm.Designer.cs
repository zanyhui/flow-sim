using System.Windows.Forms;
using ScottPlot;

namespace FlowSim
{
    /// <summary>
    /// MainForm 的 WinForms 设计器生成部分（Partial class）。
    /// <para>
    /// 本文件由 InitializeComponent 方法手动编写（等价于设计器自动生成），
    /// 完成所有控件的创建、布局和初始属性赋值。
    /// 分为四个主要功能区（选项卡）：
    /// <list type="bullet">
    ///   <item><b>河道设置</b>：输入河道几何参数（长度、宽度、糙率、床底高程等）；</item>
    ///   <item><b>边界条件</b>：选择上下游边界类型及对应参数（峰值流量、起涨时间、水深等）；</item>
    ///   <item><b>求解器设置</b>：选择格式（Preissmann/Lax）及时间步长、空间步长等；</item>
    ///   <item><b>结果</b>：显示流量过程线图、水面纵剖面图和统计汇总表格。</item>
    /// </list>
    /// 底部固定面板包含"运行仿真"按钮、"保存结果"按钮和日志文本框。
    /// </para>
    /// </summary>
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 释放 WinForms 托管资源（组件容器）。
        /// 由 WinForms 框架在窗体关闭时自动调用。
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null)) components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// 初始化所有控件（由构造函数调用）。
        /// 按功能分区创建控件并加入对应选项卡或面板。
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();

            // 主选项卡控件及七个选项页
            this.tabControl  = new TabControl();
            this.tabChannel  = new TabPage("河道设置");
            this.tabBoundary = new TabPage("边界条件");
            this.tabSolver   = new TabPage("求解器设置");
            this.tabResults  = new TabPage("结果");
            this.tabCharts   = new TabPage("图表");
            this.tabData     = new TabPage("数据");
            this.tabLayout   = new TabPage("平面图");

            // ---- 河道设置选项卡控件 ----
            this.numLength      = new NumericUpDown();   // 河道长度（m）
            this.numWidth       = new NumericUpDown();   // 河道宽度（m）
            this.numRoughness   = new NumericUpDown();   // 曼宁糙率 n
            this.numInitialFlow = new NumericUpDown();   // 初始流量（m³/s）
            this.numUsBedLevel  = new NumericUpDown();   // 上游床底高程（m）
            this.numDsBedLevel  = new NumericUpDown();   // 下游床底高程（m）
            // 不规则断面控件
            this.cmbXsType    = new ComboBox();
            this.btnLoadXsPts = new Button();   // 加载断面_测点 CSV
            this.lblXsPtsFile = new Label();    // 显示测点文件名
            this.btnLoadXsIdx = new Button();   // 加载断面_索引 CSV
            this.lblXsIdxFile = new Label();    // 显示索引文件名
            // 不规则断面预览控件
            this.cmbXsPreview   = new ComboBox();   // 断面名称选择下拉框
            this.plotXsPreview  = new FormsPlot();  // 断面形状预览图

            // ---- 边界条件选项卡控件 ----
            this.cmbUsBcType = new ComboBox();           // 上游边界类型下拉框
            this.cmbDsBcType = new ComboBox();           // 下游边界类型下拉框
            this.numPeakFlow = new NumericUpDown();      // 峰值流量（m³/s）
            this.numRiseTime = new NumericUpDown();      // 起涨时间（小时）
            this.numDsDepth  = new NumericUpDown();      // 下游初始/固定水深（m）
            // 集总调蓄库控件
            this.chkLumpedStorage = new CheckBox();
            this.numLsYMin         = new NumericUpDown();
            this.numLsYMax         = new NumericUpDown();
            this.numLsSurfaceArea  = new NumericUpDown();
            this.cmbLsRcType       = new ComboBox();
            this.numLsRcA          = new NumericUpDown();
            this.numLsRcB          = new NumericUpDown();
            this.numLsRcShift      = new NumericUpDown();

            // ---- 求解器设置选项卡控件 ----
            this.cmbSolverMethod  = new ComboBox();       // 格式选择（Preissmann/Lax）
            this.numTimeStep      = new NumericUpDown();  // 时间步长（s）
            this.numSpatialStep   = new NumericUpDown();  // 空间步长（m）
            this.numSimTime       = new NumericUpDown();  // 模拟时长（小时）
            this.numTheta         = new NumericUpDown();  // Preissmann θ 参数
            this.numTolerance     = new NumericUpDown();  // 牛顿迭代收敛容差
            this.numMaxIter       = new NumericUpDown();  // 牛顿迭代最大次数

            // ---- 结果选项卡控件 ----
            this.plotFlow    = new FormsPlot();          // 流量过程线图表
            this.plotProfile = new FormsPlot();          // 水面纵剖面图表
            this.gridSummary = new DataGridView();       // 统计汇总表格
            this.gridCfl     = new DataGridView();       // CFL 条件查看表格

            // ---- 图表选项卡控件 ----
            this.plotLongProfile = new FormsPlot();      // 纵断面水位-时间图
            this.plotXsShape     = new FormsPlot();      // 横断面形状图
            this.trkLongTime     = new TrackBar();       // 纵断面时间滑块
            this.trkXsTime       = new TrackBar();       // 横断面时间滑块
            this.lblLongTime     = new Label();          // 纵断面当前时间显示
            this.lblXsTime       = new Label();          // 横断面当前时间显示
            this.cmbXsNode       = new ComboBox();       // 断面节点选择下拉框

            // ---- 数据选项卡控件 ----
            this.trkDataTime = new TrackBar();           // 数据表时间滑块
            this.lblDataTime = new Label();              // 数据表当前时间显示
            this.gridData    = new DataGridView();       // 节点水动力数据表

            // ---- 平面图选项卡控件 ----
            this.plotChannelLayout = new FormsPlot();    // 河道平面布置图

            // ---- 底部固定面板控件 ----
            this.btnRun          = new Button();                 // "运行仿真"按钮
            this.btnSave         = new Button();                 // "保存结果"按钮
            this.btnGerdRoseires = new Button();                 // "GERD-Roseires 案例"按钮
            this.btnConfluence   = new Button();                 // "支流汇流"按钮
            this.txtLog          = new TextBox();                // 日志文本框

            this.SuspendLayout();   // 暂停布局计算，提升初始化性能

            // ===== 主选项卡控件布局 =====
            tabControl.Dock = DockStyle.Fill;
            tabControl.TabPages.Add(tabChannel);
            tabControl.TabPages.Add(tabBoundary);
            tabControl.TabPages.Add(tabSolver);
            tabControl.TabPages.Add(tabResults);
            tabControl.TabPages.Add(tabCharts);
            tabControl.TabPages.Add(tabData);
            tabControl.TabPages.Add(tabLayout);

            // ===== 河道设置选项卡 =====
            // 使用 TableLayoutPanel 两列均分布局（标签列 + 输入控件列）
            var pnlChannel = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 10,
                Padding = new System.Windows.Forms.Padding(10)
            };
            pnlChannel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlChannel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            // 局部辅助函数：向 TableLayoutPanel 添加"标签 + 数字输入框"行
            void AddRow(TableLayoutPanel p, string label, NumericUpDown num)
            {
                p.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true });
                p.Controls.Add(num);
            }

            // 配置各数字输入框的范围、精度和默认值
            ConfigNum(numLength,      20000,   100,    100000, 0, 20000);    // 河道长度默认 20 km
            ConfigNum(numWidth,       250,     1,      10000,  0, 250);      // 宽度默认 250 m
            ConfigNum(numRoughness,   0.027m,  0.001m, 0.5m,   3, 0.027m);  // 糙率默认 0.027
            ConfigNum(numInitialFlow, 250,     0,      100000, 0, 250);      // 初始流量默认 250 m³/s
            ConfigNum(numUsBedLevel,  10,      -1000,  10000,  1, 10);      // 上游床底高程默认 10 m
            ConfigNum(numDsBedLevel,  8,       -1000,  10000,  1, 8);       // 下游床底高程默认 8 m

            AddRow(pnlChannel, "河道长度（m）：",    numLength);
            AddRow(pnlChannel, "河道宽度（m）：",    numWidth);
            AddRow(pnlChannel, "曼宁糙率 n：",       numRoughness);
            AddRow(pnlChannel, "初始流量（m³/s）：", numInitialFlow);
            AddRow(pnlChannel, "上游床底高程（m）：", numUsBedLevel);
            AddRow(pnlChannel, "下游床底高程（m）：", numDsBedLevel);

            // 断面类型选择
            cmbXsType.Items.AddRange(new[] { "梯形断面", "不规则断面" });
            cmbXsType.SelectedIndex = 0;
            cmbXsType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbXsType.Dock = DockStyle.Fill;
            cmbXsType.SelectedIndexChanged += (s, e) =>
            {
                int mode = cmbXsType.SelectedIndex;
                bool isIrr  = mode == 1;
                bool isTrap = mode == 0;
                // 不规则断面模式：启用 CSV 加载按钮，禁用梯形参数（由断面数据自动提供）
                btnLoadXsPts.Enabled = btnLoadXsIdx.Enabled = isIrr;
                numWidth.Enabled      = isTrap;
                numRoughness.Enabled  = isTrap;
                numUsBedLevel.Enabled = isTrap;
                numDsBedLevel.Enabled = isTrap;
                numLength.Enabled     = isTrap;
                UpdateRunButton();
            };

            // 断面_测点 CSV 加载行：按钮 + 文件名标签
            var pnlCsvPts = new Panel { Dock = DockStyle.Fill };
            btnLoadXsPts.Text     = "加载测点…";
            btnLoadXsPts.Enabled  = false;
            btnLoadXsPts.Location = new System.Drawing.Point(0, 2);
            btnLoadXsPts.Size     = new System.Drawing.Size(90, 24);
            btnLoadXsPts.Click   += btnLoadXsPts_Click;
            lblXsPtsFile.Text     = "(未选择)";
            lblXsPtsFile.Location = new System.Drawing.Point(96, 5);
            lblXsPtsFile.AutoSize = true;
            pnlCsvPts.Controls.Add(btnLoadXsPts);
            pnlCsvPts.Controls.Add(lblXsPtsFile);

            // 断面_索引 CSV 加载行：按钮 + 文件名标签
            var pnlCsvIdx = new Panel { Dock = DockStyle.Fill };
            btnLoadXsIdx.Text     = "加载索引…";
            btnLoadXsIdx.Enabled  = false;
            btnLoadXsIdx.Location = new System.Drawing.Point(0, 2);
            btnLoadXsIdx.Size     = new System.Drawing.Size(90, 24);
            btnLoadXsIdx.Click   += btnLoadXsIdx_Click;
            lblXsIdxFile.Text     = "(未选择)";
            lblXsIdxFile.Location = new System.Drawing.Point(96, 5);
            lblXsIdxFile.AutoSize = true;
            pnlCsvIdx.Controls.Add(btnLoadXsIdx);
            pnlCsvIdx.Controls.Add(lblXsIdxFile);

            AddRow2(pnlChannel, "断面类型：",      cmbXsType);
            AddRow2(pnlChannel, "断面_测点 CSV：", pnlCsvPts);
            AddRow2(pnlChannel, "断面_索引 CSV：", pnlCsvIdx);

            // 断面预览行：断面选择下拉框 + 预览图
            cmbXsPreview.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbXsPreview.Dock = DockStyle.Fill;
            cmbXsPreview.Enabled = false;
            cmbXsPreview.SelectedIndexChanged += cmbXsPreview_SelectedIndexChanged;
            AddRow2(pnlChannel, "预览断面：", cmbXsPreview);

            // 预览图 - 跨两列，显示断面形状
            plotXsPreview.Dock = DockStyle.Fill;
            plotXsPreview.MinimumSize = new System.Drawing.Size(0, 180);
            pnlChannel.SetColumnSpan(plotXsPreview, 2);
            pnlChannel.Controls.Add(plotXsPreview);

            tabChannel.Controls.Add(pnlChannel);

            // ===== 边界条件选项卡 =====
            var pnlBoundary = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                ColumnCount = 2,
                RowCount = 20,
                AutoSize = true,
                Padding = new System.Windows.Forms.Padding(10)
            };
            pnlBoundary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlBoundary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            // 上游边界类型：流量过程线 or 正常水深
            cmbUsBcType.Items.AddRange(new[] { "流量过程线", "正常水深" });
            cmbUsBcType.SelectedIndex = 0;
            cmbUsBcType.DropDownStyle = ComboBoxStyle.DropDownList;

            // 下游边界类型：正常水深 or 固定水深
            cmbDsBcType.Items.AddRange(new[] { "正常水深", "固定水深" });
            cmbDsBcType.SelectedIndex = 0;
            cmbDsBcType.DropDownStyle = ComboBoxStyle.DropDownList;

            ConfigNum(numPeakFlow, 1000,   0,      1000000, 0,    1000);    // 峰值流量默认 1000 m³/s
            ConfigNum(numRiseTime, 2,      0.1m,   100,     1,    2);       // 起涨时间默认 2 h
            ConfigNum(numDsDepth,  3,      0.01m,  1000,    2,    3);       // 下游水深默认 3 m

            // 局部辅助函数：添加"标签 + 任意控件"行
            void AddRow2(TableLayoutPanel p, string label, Control ctrl)
            {
                p.Controls.Add(new Label { Text = label, Anchor = AnchorStyles.Left | AnchorStyles.Right, AutoSize = true });
                p.Controls.Add(ctrl);
            }

            // 上游区分隔标题
            pnlBoundary.Controls.Add(new Label { Text = "─── 上游 ───", Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold) });
            pnlBoundary.Controls.Add(new Label());   // 占位（右列空）
            AddRow2(pnlBoundary, "上游边界条件类型：", cmbUsBcType);
            AddRow2(pnlBoundary, "峰值流量（m³/s）：", numPeakFlow);
            AddRow2(pnlBoundary, "起涨时间（小时）：", numRiseTime);

            // 下游区分隔标题
            pnlBoundary.Controls.Add(new Label { Text = "─── 下游 ───", Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold) });
            pnlBoundary.Controls.Add(new Label());
            AddRow2(pnlBoundary, "下游边界条件类型：",  cmbDsBcType);
            AddRow2(pnlBoundary, "初始/固定水深（m）：", numDsDepth);

            // ── 集总调蓄库（LumpedStorage）区 ──
            pnlBoundary.Controls.Add(new Label
            {
                Text = "─── 集总调蓄库 ───",
                Font = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
            });
            chkLumpedStorage.Text = "启用下游调蓄库 (LumpedStorage)";
            chkLumpedStorage.AutoSize = true;
            chkLumpedStorage.Dock = DockStyle.Fill;
            chkLumpedStorage.CheckedChanged += chkLumpedStorage_CheckedChanged;
            pnlBoundary.Controls.Add(chkLumpedStorage);

            ConfigNum(numLsYMin,       5.0m,  -1000m, 10000m, 1, 1m);
            ConfigNum(numLsYMax,       12.0m, -1000m, 10000m, 1, 1m);
            ConfigNum(numLsSurfaceArea, 500000m, 1m, 999000000m, 0, 50000m);
            cmbLsRcType.Items.AddRange(new[] { "无出流", "幂律  Q = A·(Z+shift)^B" });
            cmbLsRcType.SelectedIndex = 0;
            cmbLsRcType.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbLsRcType.Dock = DockStyle.Fill;
            cmbLsRcType.SelectedIndexChanged += (s, e) =>
            {
                bool hasCurve = cmbLsRcType.SelectedIndex > 0 && chkLumpedStorage.Checked;
                numLsRcA.Enabled = numLsRcB.Enabled = numLsRcShift.Enabled = hasCurve;
            };
            ConfigNum(numLsRcA,     50m,   0.001m, 99000000m, 3, 10m);
            ConfigNum(numLsRcB,     1.5m,  0.01m,  10m,       2, 0.1m);
            ConfigNum(numLsRcShift, -5.0m, -1000m, 0m,        2, 0.5m);

            AddRow2(pnlBoundary, "最低水位 yMin（m）：", numLsYMin);
            AddRow2(pnlBoundary, "最高水位 yMax（m）：", numLsYMax);
            AddRow2(pnlBoundary, "水库水面积（m²）：",   numLsSurfaceArea);
            AddRow2(pnlBoundary, "出口曲线类型：",       cmbLsRcType);
            AddRow2(pnlBoundary, "曲线系数 A：",         numLsRcA);
            AddRow2(pnlBoundary, "曲线指数 B：",         numLsRcB);
            AddRow2(pnlBoundary, "水位偏移 shift：",     numLsRcShift);

            // 初始禁用调蓄库控件（勾选后启用）
            numLsYMin.Enabled = numLsYMax.Enabled = numLsSurfaceArea.Enabled =
            cmbLsRcType.Enabled = numLsRcA.Enabled = numLsRcB.Enabled = numLsRcShift.Enabled = false;

            // 放入可滚动容器（控件多时支持滚动查看）
            var scrollBoundary = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            scrollBoundary.Controls.Add(pnlBoundary);
            tabBoundary.Controls.Add(scrollBoundary);

            // ===== 求解器设置选项卡 =====
            var pnlSolver = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 8,
                Padding = new System.Windows.Forms.Padding(10)
            };
            pnlSolver.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            pnlSolver.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));

            // 格式选择下拉框；选 Lax 时禁用 θ/tolerance/maxIter 参数（仅 Preissmann 使用）
            cmbSolverMethod.Items.AddRange(new[] { "Preissmann", "Lax-Friedrichs" });
            cmbSolverMethod.SelectedIndex = 0;
            cmbSolverMethod.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbSolverMethod.SelectedIndexChanged += (s, e) =>
            {
                bool isPreissmann = cmbSolverMethod.SelectedIndex == 0;
                numTheta.Enabled     = isPreissmann;
                numTolerance.Enabled = isPreissmann;
                numMaxIter.Enabled   = isPreissmann;
            };

            ConfigNum(numTimeStep,    60,    1,       3600,   0,    60);      // 时间步长默认 60 s
            ConfigNum(numSpatialStep, 500,   10,      10000,  0,    500);     // 空间步长默认 500 m
            ConfigNum(numSimTime,     24,    1,       720,    0,    24);      // 模拟时长默认 24 h
            ConfigNum(numTheta,       0.6m,  0.5m,   1.0m,   2,    0.05m);   // θ 默认 0.6（步长 0.05，原值 0.6 过大）
            // 收敛容差：科学计数形式用 decimal，精度 8 位小数，步长 0.0001
            numTolerance.Minimum       = 1e-10m;
            numTolerance.Maximum       = 1m;
            numTolerance.DecimalPlaces = 8;
            numTolerance.Increment     = 0.0001m;
            numTolerance.Value         = 0.0001m;   // 默认 1e-4
            numTolerance.Dock          = DockStyle.Fill;
            ConfigNum(numMaxIter,     100,   1,       10000,  0,    10);      // 最大迭代默认 100

            AddRow(pnlSolver, "时间步长（s）：", numTimeStep);
            pnlSolver.Controls.Add(new Label { Text = "求解方法：", AutoSize = true });
            pnlSolver.Controls.Add(cmbSolverMethod);
            AddRow(pnlSolver, "空间步长（m）：",          numSpatialStep);
            AddRow(pnlSolver, "模拟时长（小时）：",       numSimTime);
            AddRow(pnlSolver, "θ 参数（Preissmann）：",   numTheta);
            AddRow(pnlSolver, "收敛容差（Preissmann）：", numTolerance);
            AddRow(pnlSolver, "最大迭代次数（Preissmann）：", numMaxIter);

            tabSolver.Controls.Add(pnlSolver);

            // ===== 结果选项卡 =====
            // 上半部分：左右两个图表（水平 SplitContainer）
            // 下半部分：选项卡（统计汇总 + CFL 条件查看）
            var splitResults = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Horizontal,
                SplitterDistance = 350   // 分割条距顶部 350 px
            };

            var splitPlots = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Vertical   // 左右分割
            };

            plotFlow.Dock    = DockStyle.Fill;   // 左侧：流量过程线
            plotProfile.Dock = DockStyle.Fill;   // 右侧：水面纵剖面
            splitPlots.Panel1.Controls.Add(plotFlow);
            splitPlots.Panel2.Controls.Add(plotProfile);

            // 统计汇总表格配置：两列（参数名 + 数值），只读
            gridSummary.Dock           = DockStyle.Fill;
            gridSummary.ColumnCount    = 2;
            gridSummary.Columns[0].Name  = "参数";
            gridSummary.Columns[1].Name  = "数值";
            gridSummary.Columns[0].Width = 250;
            gridSummary.Columns[1].Width = 150;
            gridSummary.AllowUserToAddRows = false;
            gridSummary.ReadOnly           = true;
            gridSummary.AutoSizeRowsMode   = DataGridViewAutoSizeRowsMode.AllCells;

            // CFL 条件查看表格：仅 Lax 格式有效，显示每步最大 CFL
            gridCfl.Dock           = DockStyle.Fill;
            gridCfl.ColumnCount    = 3;
            gridCfl.Columns[0].Name  = "时步";
            gridCfl.Columns[1].Name  = "模拟时刻（h）";
            gridCfl.Columns[2].Name  = "最大 CFL";
            gridCfl.Columns[0].Width = 80;
            gridCfl.Columns[1].Width = 120;
            gridCfl.Columns[2].Width = 120;
            gridCfl.AllowUserToAddRows = false;
            gridCfl.ReadOnly           = true;
            gridCfl.AutoSizeRowsMode   = DataGridViewAutoSizeRowsMode.AllCells;

            // 用选项卡将两个表格放在下半区，方便切换
            var tabBottom = new TabControl { Dock = DockStyle.Fill };
            var tabSummary = new TabPage("统计汇总");
            var tabCfl     = new TabPage("CFL 条件");
            tabSummary.Controls.Add(gridSummary);
            tabCfl.Controls.Add(gridCfl);
            tabBottom.TabPages.Add(tabSummary);
            tabBottom.TabPages.Add(tabCfl);

            splitResults.Panel1.Controls.Add(splitPlots);
            splitResults.Panel2.Controls.Add(tabBottom);

            tabResults.Controls.Add(splitResults);

            // ===== 图表选项卡 =====
            // 上半：纵断面水位图（可拖动时间滑块）
            // 下半：横断面形状图（可选节点 + 拖动时间滑块）
            var splitCharts = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = System.Windows.Forms.Orientation.Horizontal,
                SplitterDistance = 320   // 上下各约一半
            };

            // ── 纵断面区（上半）──
            // 控制面板：标题 + 时间滑块 + 当前时间标签
            var pnlLongCtrl = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new System.Windows.Forms.Padding(5, 6, 5, 0) };

            var lblLongTitle = new Label
            {
                Text      = "纵断面水深",
                Location  = new System.Drawing.Point(5, 12),
                AutoSize  = true,
                Font      = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
            };

            var lblLongSlider = new Label { Text = "时间：", Location = new System.Drawing.Point(110, 14), AutoSize = true };

            trkLongTime.Minimum       = 0;
            trkLongTime.Maximum       = 100;
            trkLongTime.Value         = 0;
            trkLongTime.TickFrequency = 10;
            trkLongTime.AutoSize      = false;
            trkLongTime.Location      = new System.Drawing.Point(158, 4);
            trkLongTime.Size          = new System.Drawing.Size(400, 42);
            trkLongTime.Enabled       = false;
            trkLongTime.Scroll       += trkLongTime_Scroll;

            lblLongTime.Text     = "—";
            lblLongTime.Location = new System.Drawing.Point(570, 14);
            lblLongTime.AutoSize = true;

            pnlLongCtrl.Controls.Add(lblLongTitle);
            pnlLongCtrl.Controls.Add(lblLongSlider);
            pnlLongCtrl.Controls.Add(trkLongTime);
            pnlLongCtrl.Controls.Add(lblLongTime);

            plotLongProfile.Dock = DockStyle.Fill;

            splitCharts.Panel1.Controls.Add(plotLongProfile);   // Fill (底层先加)
            splitCharts.Panel1.Controls.Add(pnlLongCtrl);       // Top (后加，置于顶部)

            // ── 横断面区（下半）──
            var pnlXsCtrl = new Panel { Dock = DockStyle.Top, Height = 50, Padding = new System.Windows.Forms.Padding(5, 6, 5, 0) };

            var lblXsTitle = new Label
            {
                Text     = "断面形状",
                Location = new System.Drawing.Point(5, 12),
                AutoSize = true,
                Font     = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
            };

            var lblXsNodeLabel = new Label { Text = "节点：", Location = new System.Drawing.Point(95, 14), AutoSize = true };

            cmbXsNode.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbXsNode.Location      = new System.Drawing.Point(140, 10);
            cmbXsNode.Size          = new System.Drawing.Size(180, 24);
            cmbXsNode.Enabled       = false;
            cmbXsNode.SelectedIndexChanged += cmbXsNode_SelectedIndexChanged;

            var lblXsSlider = new Label { Text = "时间：", Location = new System.Drawing.Point(332, 14), AutoSize = true };

            trkXsTime.Minimum       = 0;
            trkXsTime.Maximum       = 100;
            trkXsTime.Value         = 0;
            trkXsTime.TickFrequency = 10;
            trkXsTime.AutoSize      = false;
            trkXsTime.Location      = new System.Drawing.Point(378, 4);
            trkXsTime.Size          = new System.Drawing.Size(350, 42);
            trkXsTime.Enabled       = false;
            trkXsTime.Scroll       += trkXsTime_Scroll;

            lblXsTime.Text     = "—";
            lblXsTime.Location = new System.Drawing.Point(738, 14);
            lblXsTime.AutoSize = true;

            pnlXsCtrl.Controls.Add(lblXsTitle);
            pnlXsCtrl.Controls.Add(lblXsNodeLabel);
            pnlXsCtrl.Controls.Add(cmbXsNode);
            pnlXsCtrl.Controls.Add(lblXsSlider);
            pnlXsCtrl.Controls.Add(trkXsTime);
            pnlXsCtrl.Controls.Add(lblXsTime);

            plotXsShape.Dock = DockStyle.Fill;

            splitCharts.Panel2.Controls.Add(plotXsShape);   // Fill
            splitCharts.Panel2.Controls.Add(pnlXsCtrl);     // Top

            tabCharts.Controls.Add(splitCharts);

            // ===== 数据选项卡 =====
            // 顶部控制面板：时间滑块 + 当前时刻标签
            var pnlDataCtrl = new Panel { Dock = DockStyle.Top, Height = 46, Padding = new System.Windows.Forms.Padding(8, 8, 8, 0) };

            var lblDataTitle = new Label
            {
                Text      = "节点水动力数据",
                Location  = new System.Drawing.Point(8, 14),
                AutoSize  = true,
                Font      = new System.Drawing.Font("Segoe UI", 9, System.Drawing.FontStyle.Bold)
            };

            var lblDataSlider = new Label { Text = "时间：", Location = new System.Drawing.Point(175, 16), AutoSize = true };

            trkDataTime.Minimum       = 0;
            trkDataTime.Maximum       = 100;
            trkDataTime.Value         = 0;
            trkDataTime.TickFrequency = 10;
            trkDataTime.AutoSize      = false;
            trkDataTime.Location      = new System.Drawing.Point(225, 6);
            trkDataTime.Size          = new System.Drawing.Size(450, 38);
            trkDataTime.Enabled       = false;
            trkDataTime.Scroll       += trkDataTime_Scroll;

            lblDataTime.Text     = "—";
            lblDataTime.Location = new System.Drawing.Point(685, 16);
            lblDataTime.AutoSize = true;

            pnlDataCtrl.Controls.Add(lblDataTitle);
            pnlDataCtrl.Controls.Add(lblDataSlider);
            pnlDataCtrl.Controls.Add(trkDataTime);
            pnlDataCtrl.Controls.Add(lblDataTime);

            // 数据表格：只读，多列显示节点水动力状态
            gridData.Dock               = DockStyle.Fill;
            gridData.AllowUserToAddRows = false;
            gridData.ReadOnly           = true;
            gridData.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            gridData.SelectionMode      = DataGridViewSelectionMode.FullRowSelect;
            gridData.RowHeadersVisible  = false;
            gridData.ColumnCount        = 9;
            gridData.Columns[0].Name    = "节点";
            gridData.Columns[1].Name    = "桩号 (km)";
            gridData.Columns[2].Name    = "流量 (m³/s)";
            gridData.Columns[3].Name    = "水位 (m)";
            gridData.Columns[4].Name    = "水深 (m)";
            gridData.Columns[5].Name    = "过水面积 (m²)";
            gridData.Columns[6].Name    = "水面宽 (m)";
            gridData.Columns[7].Name    = "流速 (m/s)";
            gridData.Columns[8].Name    = "Fr";
            // 节点列较窄，其余列均匀
            gridData.Columns[0].FillWeight = 50;
            gridData.Columns[1].FillWeight = 80;
            for (int col = 2; col < 9; col++) gridData.Columns[col].FillWeight = 100;
            gridData.AlternatingRowsDefaultCellStyle.BackColor = System.Drawing.Color.AliceBlue;

            tabData.Controls.Add(gridData);     // Fill（先加）
            tabData.Controls.Add(pnlDataCtrl);  // Top（后加）

            // ===== 平面图选项卡 =====
            // 充满整个选项页，仅含一个 ScottPlot 图表控件
            plotChannelLayout.Dock = DockStyle.Fill;
            tabLayout.Controls.Add(plotChannelLayout);

            // ===== 底部固定面板（按钮 + 日志）=====
            // "运行仿真"按钮（蓝色主按钮）
            btnRun.Text      = "▶ 运行仿真";
            btnRun.Size      = new System.Drawing.Size(150, 35);
            btnRun.Location  = new System.Drawing.Point(10, 5);
            btnRun.Click    += btnRun_Click;
            btnRun.BackColor = System.Drawing.Color.FromArgb(0, 122, 204);
            btnRun.ForeColor = System.Drawing.Color.White;
            btnRun.FlatStyle = FlatStyle.Flat;

            // "保存结果"按钮（仿真完成前禁用）
            btnSave.Text      = "💾 保存结果";
            btnSave.Size      = new System.Drawing.Size(150, 35);
            btnSave.Location  = new System.Drawing.Point(170, 5);
            btnSave.Click    += btnSave_Click;
            btnSave.Enabled   = false;
            btnSave.FlatStyle = FlatStyle.Flat;

            // "GERD-Roseires 案例"按钮（打开专用窗体）
            btnGerdRoseires.Text      = "🏞 GERD-Roseires 案例";
            btnGerdRoseires.Size      = new System.Drawing.Size(180, 35);
            btnGerdRoseires.Location  = new System.Drawing.Point(330, 5);
            btnGerdRoseires.Click    += btnGerdRoseires_Click;
            btnGerdRoseires.BackColor = System.Drawing.Color.FromArgb(0, 153, 76);
            btnGerdRoseires.ForeColor = System.Drawing.Color.White;
            btnGerdRoseires.FlatStyle = FlatStyle.Flat;

            // "支流汇流"按钮（打开支流汇流仿真专用窗体）
            btnConfluence.Text      = "🌊 支流汇流";
            btnConfluence.Size      = new System.Drawing.Size(140, 35);
            btnConfluence.Location  = new System.Drawing.Point(520, 5);
            btnConfluence.Click    += btnConfluence_Click;
            btnConfluence.BackColor = System.Drawing.Color.FromArgb(0, 100, 180);
            btnConfluence.ForeColor = System.Drawing.Color.White;
            btnConfluence.FlatStyle = FlatStyle.Flat;

            // 日志文本框（黑色背景、绿色字体，模拟终端风格）
            txtLog.Multiline    = true;
            txtLog.ScrollBars   = ScrollBars.Vertical;
            txtLog.ReadOnly     = true;
            txtLog.Location     = new System.Drawing.Point(10, 45);
            txtLog.Width        = 760;
            txtLog.Height       = 80;
            txtLog.BackColor    = System.Drawing.Color.Black;
            txtLog.ForeColor    = System.Drawing.Color.Lime;
            txtLog.Font         = new System.Drawing.Font("Consolas", 8);

            var pnlBottom = new Panel { Dock = DockStyle.Bottom, Height = 135 };
            pnlBottom.Controls.Add(btnRun);
            pnlBottom.Controls.Add(btnSave);
            pnlBottom.Controls.Add(btnGerdRoseires);
            pnlBottom.Controls.Add(btnConfluence);
            pnlBottom.Controls.Add(txtLog);

            // ===== 主窗体设置 =====
            this.Text        = "FlowSim – 一维水动力仿真";
            this.Size        = new System.Drawing.Size(900, 650);
            this.MinimumSize = new System.Drawing.Size(700, 500);
            this.Controls.Add(tabControl);
            this.Controls.Add(pnlBottom);

            this.ResumeLayout(false);   // 恢复布局计算并立即执行
        }

        /// <summary>
        /// 配置数字输入框（NumericUpDown）的通用参数。
        /// 统一设置最小值、最大值、小数位数、步长、当前值和停靠样式。
        /// </summary>
        /// <param name="num">要配置的 NumericUpDown 控件。</param>
        /// <param name="value">初始值（当前值）。</param>
        /// <param name="min">允许的最小值。</param>
        /// <param name="max">允许的最大值。</param>
        /// <param name="decimals">小数位数（0 表示整数）。</param>
        /// <param name="increment">每次点击增减的步长。</param>
        private static void ConfigNum(NumericUpDown num, decimal value, decimal min, decimal max, int decimals, decimal increment)
        {
            num.Minimum       = min;
            num.Maximum       = max;
            num.DecimalPlaces = decimals;
            num.Increment     = increment;
            num.Value         = value;
            num.Dock          = DockStyle.Fill;   // 充满所在单元格
        }

        // ---- 控件字段声明 ----
        private TabControl tabControl;
        private TabPage tabChannel, tabBoundary, tabSolver, tabResults, tabCharts, tabData, tabLayout;
        private NumericUpDown numLength, numWidth, numRoughness, numInitialFlow;
        private NumericUpDown numUsBedLevel, numDsBedLevel;
        private ComboBox cmbUsBcType, cmbDsBcType;
        private NumericUpDown numPeakFlow, numRiseTime, numDsDepth;
        private ComboBox cmbSolverMethod;
        private NumericUpDown numTimeStep, numSpatialStep, numSimTime, numTheta;
        private NumericUpDown numTolerance, numMaxIter;
        private FormsPlot plotFlow, plotProfile;
        private DataGridView gridSummary;
        private DataGridView gridCfl;
        private Button btnRun, btnSave, btnGerdRoseires, btnConfluence;
        private TextBox txtLog;
        // 不规则断面控件
        private ComboBox cmbXsType;
        private Button btnLoadXsPts;   // 加载断面_测点 CSV
        private Label  lblXsPtsFile;   // 显示测点文件名
        private Button btnLoadXsIdx;   // 加载断面_索引 CSV
        private Label  lblXsIdxFile;   // 显示索引文件名
        // 不规则断面预览控件
        private ComboBox  cmbXsPreview;   // 断面名称选择下拉框
        private FormsPlot plotXsPreview;  // 断面形状预览图
        // 集总调蓄库控件
        private CheckBox chkLumpedStorage;
        private NumericUpDown numLsYMin, numLsYMax, numLsSurfaceArea;
        private NumericUpDown numLsRcA, numLsRcB, numLsRcShift;
        private ComboBox cmbLsRcType;
        // 图表选项卡控件
        private FormsPlot plotLongProfile, plotXsShape;
        private TrackBar trkLongTime, trkXsTime;
        private Label lblLongTime, lblXsTime;
        private ComboBox cmbXsNode;
        // 数据选项卡控件
        private TrackBar trkDataTime;
        private Label lblDataTime;
        private DataGridView gridData;
        // 平面图选项卡控件
        private FormsPlot plotChannelLayout;   // 河道平面布置图

        #endregion
    }
}
