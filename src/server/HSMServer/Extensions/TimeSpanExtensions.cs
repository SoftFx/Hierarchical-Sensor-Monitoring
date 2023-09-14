using HSMCommon.Extensions;
using System;
namespace HSMServer.Extensions;

public static class TimeSpanExtensions
{
    public static string ToTableView(this string timeSpanStr) =>
        TimeSpan.TryParse(timeSpanStr, out var timeSpan) ? timeSpan.ToReadableView() : string.Empty;

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