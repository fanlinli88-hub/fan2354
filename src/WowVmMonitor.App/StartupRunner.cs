using WowVmMonitor.Core.Configuration;
using WowVmMonitor.App.Shares;

namespace WowVmMonitor.App;

public static class StartupRunner
{
    public static async Task<int> RunAsync(
        ConfigurationLoadResult loadResult,
        IShareConnectionCoordinator shareConnector,
        TextWriter output,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(loadResult);
        ArgumentNullException.ThrowIfNull(shareConnector);
        ArgumentNullException.ThrowIfNull(output);

        foreach (var message in loadResult.Messages)
        {
            await output.WriteLineAsync($"{message.Code}: {message.Message}").ConfigureAwait(false);
        }

        if (loadResult.RequiresUserConfirmation)
        {
            return 2;
        }

        var results = await shareConnector
            .ConnectEnabledAsync(loadResult.Configuration, cancellationToken)
            .ConfigureAwait(false);
        foreach (var result in results)
        {
            var errorCode = result.Win32ErrorCode is null
                ? string.Empty
                : $" Win32={result.Win32ErrorCode.Value}";
            await output
                .WriteLineAsync($"{result.MachineId}: {result.Code}{errorCode}")
                .ConfigureAwait(false);
        }

        return results.All(result => result.Succeeded) ? 0 : 1;
    }
}
