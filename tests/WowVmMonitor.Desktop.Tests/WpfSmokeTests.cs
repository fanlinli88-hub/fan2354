using WowVmMonitor.Desktop;
using WowVmMonitor.Desktop.Presentation;

namespace WowVmMonitor.Desktop.Tests;

public sealed class WpfSmokeTests
{
    [Fact]
    public void MainWindowCanBeCreatedOnStaThread()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = new MainWindow();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
    }

    [Fact]
    public void MainWindowContainsChineseOperationalLabels()
    {
        Exception? failure = null;
        string[] labels = [];
        var thread = new Thread(() =>
        {
            try
            {
                var window = new MainWindow();
                window.ApplyTemplate();
                window.UpdateLayout();
                labels = CollectLabels(window).ToArray();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.Contains("监控状态", labels);
        Assert.Contains("设置", labels);
        Assert.Contains("异常历史", labels);
        Assert.Contains("开始监控", labels);
        Assert.Contains("停止监控", labels);
        Assert.Contains("立即检测", labels);
        Assert.Contains("机器", labels);
        Assert.Contains("状态", labels);
        Assert.Contains("日志时间", labels);
        Assert.Contains("启用 ntfy", labels);
        Assert.Contains("ntfy 主题", labels);
        Assert.Contains("测试推送", labels);
    }

    [Fact]
    public void TrayAndStartupMessagesUseChineseText()
    {
        Assert.Equal("打开主窗口", DesktopPresentationText.Open);
        Assert.Equal("开始监控", DesktopPresentationText.StartMonitoring);
        Assert.Equal("停止监控", DesktopPresentationText.StopMonitoring);
        Assert.Equal("退出程序", DesktopPresentationText.Exit);
        Assert.Equal("WowVmMonitor 启动失败", DesktopPresentationText.StartupErrorTitle);
    }

    private static IEnumerable<string> CollectLabels(System.Windows.DependencyObject root)
    {
        if (root is System.Windows.Controls.ContentControl { Content: string content })
        {
            yield return content;
        }

        if (root is System.Windows.Controls.HeaderedContentControl { Header: string header })
        {
            yield return header;
        }

        if (root is System.Windows.Controls.DataGrid dataGrid)
        {
            foreach (var columnHeader in dataGrid.Columns.Select(column => column.Header).OfType<string>())
            {
                yield return columnHeader;
            }
        }

        if (root is System.Windows.Controls.TextBlock { Text: { Length: > 0 } text })
        {
            yield return text;
        }

        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(root).OfType<System.Windows.DependencyObject>())
        {
            foreach (var label in CollectLabels(child))
            {
                yield return label;
            }
        }
    }
}
