using System;
using System.Text;
namespace HSMServer.Extensions;

public static class TimeSpanExtensions
{
    public static string ToTableView(this TimeSpan time)
    {
        bool hasPrevious = false;

        string BuildUnit(string unit, int val)
        {
            if (val == 0)
                return string.Empty;

            var str = $"{val} {unit}";

            if (hasPrevious)
                str = $" {str}";

            if (val > 1)
                str = $"{str}s";

            hasPrevious = true;

            return str;
        }

        var tooltip = new StringBuilder(1 << 4);

        return tooltip.Append(BuildUnit("day", time.Days))
                      .Append(BuildUnit("hour", time.Hours))
                      .Append(BuildUnit("minute", time.Minutes))
                      .Append(BuildUnit("second", time.Seconds))
                      .ToString();
    }

    public static string ToTableView(this string timeSpanStr) =>
        TimeSpan.TryParse(timeSpanStr, out var timeSpan) ? timeSpan.ToTableView() : string.Empty;

    public static string TicksToString(this long ticks)
    {
        var timeSpan = TimeSpan.FromTicks(ticks);
        return $"{timeSpan.Days}.{timeSpan.Hours}:{timeSpan.Minutes}:{timeSpan.Seconds}";
    }

    public static bool TryParse(this string interval, out long ticks)
    {
        var ddString = interval.Split(".");
        var hmsString = ddString[^1].Split(":");

        if (ddString.Length == 2 &&
            hmsString.Length == 3 &&
            int.TryParse(ddString[0], out var days) &&
            int.TryParse(hmsString[0], out var hours) &&
            int.TryParse(hmsString[1], out var minutes) &&
            int.TryParse(hmsString[2], out var seconds))
        {
            ticks = new TimeSpan(days, hours, minutes, seconds).Ticks;
            return true;
        }

        ticks = 0L;
        return false;
    }
}