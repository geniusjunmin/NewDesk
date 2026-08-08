using System;
using System.Globalization;

namespace NewDesk.Services;

public static class ReminderTimeParser
{
    private static readonly string[] SupportedFormats = ["H:mm", "HH:mm"];

    public static bool TryParseClockTime(string? value, out TimeSpan time)
    {
        time = default;
        if (!TimeOnly.TryParseExact(
                value?.Trim(),
                SupportedFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        time = parsed.ToTimeSpan();
        return true;
    }
}
