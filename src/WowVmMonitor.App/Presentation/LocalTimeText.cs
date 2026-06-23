using System.Globalization;

namespace WowVmMonitor.App.Presentation;

public static class LocalTimeText
{
    public static string Format(DateTimeOffset? value) =>
        Format(value, TimeZoneInfo.Local);

    public static string Format(DateTimeOffset? value, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        return value is null
            ? "--"
            : TimeZoneInfo.ConvertTime(value.Value, timeZone)
                .ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
    }
}
