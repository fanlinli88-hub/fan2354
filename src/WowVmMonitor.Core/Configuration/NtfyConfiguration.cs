using System.Text.RegularExpressions;

namespace WowVmMonitor.Core.Configuration;

public sealed record NtfyConfiguration(bool Enabled, string Topic)
{
    public const string DefaultTopic = "wow-vm-85898-fan2354";
    private static readonly Regex TopicPattern = new(
        "^[A-Za-z0-9_-]{1,64}$",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);

    public static NtfyConfiguration CreateDefault() => new(false, DefaultTopic);

    public static bool IsValidTopic(string? topic) =>
        topic is not null && TopicPattern.IsMatch(topic);
}
