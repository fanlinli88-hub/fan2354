using WowVmMonitor.App.Presentation;

namespace WowVmMonitor.Tests.App;

public sealed class LocalTimeTextTests
{
    [Fact]
    public void UtcTimestampIsFormattedInRequestedLocalTimeZone()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "China Standard Test Time",
            TimeSpan.FromHours(8),
            "China Standard Test Time",
            "China Standard Test Time");
        var value = new DateTimeOffset(2026, 6, 19, 3, 1, 28, TimeSpan.Zero);

        Assert.Equal("2026-06-19 11:01:28", LocalTimeText.Format(value, zone));
    }

    [Fact]
    public void MissingTimestampUsesPlaceholder() =>
        Assert.Equal("--", LocalTimeText.Format(null, TimeZoneInfo.Utc));
}
