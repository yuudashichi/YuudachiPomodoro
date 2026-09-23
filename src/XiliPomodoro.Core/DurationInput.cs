using System.Globalization;

namespace XiliPomodoro.Core;

public static class DurationInput
{
    public static bool TryParse(string text, out int seconds)
    {
        seconds = 0;
        var parts = text.Trim().Replace('：', ':').Split(':');
        if (parts.Length is < 1 or > 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes) || minutes > 180) return false;
        var remainder = 0;
        if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out remainder) || remainder >= 60)) return false;
        seconds = minutes * 60 + remainder;
        return seconds is >= 60 and <= 10800;
    }
}
