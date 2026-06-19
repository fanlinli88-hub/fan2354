using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.App.Presentation;

public static class MonitorStatusText
{
    public static string Format(VmMonitorStatus status) => status switch
    {
        VmMonitorStatus.Normal => "正常",
        VmMonitorStatus.Warning => "预警",
        VmMonitorStatus.Alert => "异常",
        VmMonitorStatus.Recovery => "已恢复",
        VmMonitorStatus.ShareUnavailable => "共享不可访问",
        VmMonitorStatus.NoLog => "未找到日志",
        VmMonitorStatus.Error => "检测错误",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unsupported monitor status.")
    };

    public static string Format(MonitorTransition transition) => transition switch
    {
        MonitorTransition.Alert => "异常告警",
        MonitorTransition.Recovery => "恢复正常",
        MonitorTransition.None => "无变化",
        _ => throw new ArgumentOutOfRangeException(nameof(transition), transition, "Unsupported monitor transition.")
    };
}
