using WowVmMonitor.Desktop;

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
}
