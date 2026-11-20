using System;
using System.Globalization;

namespace HSMServer.ServerConfiguration
{
    // The "HH:mm" UTC times of day of the working-hours keys (HistorySealing, HistoryCompaction).
    internal static class UtcTimeOfDay
    {
        public static TimeSpan Parse(string value, string section, string key)
        {
            if (TimeSpan.TryParseExact(value?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1))
                return time;

            throw new InvalidOperationException($"{section}.{key} = '{value}' is invalid: expected a UTC time of day as HH:mm.");
        }
    }
}
