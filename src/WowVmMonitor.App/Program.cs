using WowVmMonitor.App;
using WowVmMonitor.Infrastructure.Configuration;
using WowVmMonitor.Infrastructure.Credentials;
using WowVmMonitor.Infrastructure.Shares;

var configurationDirectory = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "WowVmMonitor");

try
{
    var loadResult = new ConfigurationStore(configurationDirectory).Load();
    var credentialStore = new WindowsCredentialStore(new WindowsCredentialNativeApi());
    var shareConnector = new ShareConnectionCoordinator(credentialStore, new WindowsNetworkApi());
    return await StartupRunner.RunAsync(
        loadResult,
        shareConnector,
        Console.Out,
        CancellationToken.None);
}
catch
{
    Console.Error.WriteLine("startup.failed: WowVmMonitor could not initialize configuration or shares.");
    return 1;
}
