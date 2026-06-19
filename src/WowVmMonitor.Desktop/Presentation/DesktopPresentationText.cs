namespace WowVmMonitor.Desktop.Presentation;

public static class DesktopPresentationText
{
    public const string Open = "打开主窗口";
    public const string StartMonitoring = "开始监控";
    public const string StopMonitoring = "停止监控";
    public const string Exit = "退出程序";
    public const string StartupErrorTitle = "WowVmMonitor 启动失败";

    public static string StartupError(string message) => $"程序无法启动：{message}";
}
