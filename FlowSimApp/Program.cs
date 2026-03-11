namespace FlowSim;

/// <summary>
/// 应用程序静态入口类。
/// 负责初始化 WinForms 运行时环境并启动主窗体。
/// </summary>
static class Program
{
    /// <summary>
    /// 应用程序的主入口点。
    /// [STAThread] 特性要求 UI 线程必须运行在单线程单元（STA）模式下，
    /// 这是 WinForms 和 COM 组件的标准要求。
    /// </summary>
    [STAThread]
    static void Main()
    {
        // 初始化应用程序配置（高 DPI、默认字体等）
        // 详见 https://aka.ms/applicationconfiguration
        ApplicationConfiguration.Initialize();

        // 创建主窗体实例并启动消息循环，直到主窗体关闭为止
        Application.Run(new MainForm());
    }    
}