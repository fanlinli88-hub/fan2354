using WowVmMonitor.App.Presentation;
using WowVmMonitor.Core.Monitoring;

namespace WowVmMonitor.Tests.App;

public sealed class MonitorStatusTextTests
{
    [Theory]
    [InlineData(VmMonitorStatus.Normal, "正常")]
    [InlineData(VmMonitorStatus.Warning, "预警")]
    [InlineData(VmMonitorStatus.Alert, "异常")]
    [InlineData(VmMonitorStatus.Recovery, "已恢复")]
    [InlineData(VmMonitorStatus.ShareUnavailable, "共享不可访问")]
    [InlineData(VmMonitorStatus.NoLog, "未找到日志")]
    [InlineData(VmMonitorStatus.Error, "检测错误")]
    public void EveryMonitorStatusHasChineseText(VmMonitorStatus status, string expected) =>
        Assert.Equal(expected, MonitorStatusText.Format(status));
}
