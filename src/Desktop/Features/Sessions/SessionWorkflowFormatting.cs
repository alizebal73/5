using System.Globalization;

namespace GameNet.Desktop.Features.Sessions;

/// <summary>Pure display formatting; it does not participate in Session or billing rules.</summary>
internal static class SessionWorkflowFormatting
{
    public static string FormatDurationDisplayText(long elapsedTicks)
    {
        if (elapsedTicks <= 0)
            return CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase)
                ? "مدت زمان در دسترس نیست" : "Duration unavailable";

        var duration = TimeSpan.FromTicks(elapsedTicks);
        var totalHours = checked((long)duration.Days * 24 + duration.Hours);
        var value = $"{totalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";
        return CultureInfo.CurrentUICulture.Name.StartsWith("fa", StringComparison.OrdinalIgnoreCase)
            ? $"مدت: {value}" : $"Duration: {value}";
    }
}
