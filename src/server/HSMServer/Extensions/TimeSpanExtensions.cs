using System;
using System.Text;
namespace HSMServer.Extensions;

public static class TimeSpanExtensions
{
    public static string ToToolTip(this TimeSpan time)
    {
        var tooltip = new StringBuilder(1 << 4);

        if (time.Days != 0)
            tooltip.Append($"{time.Days}d ");

        return tooltip.Append($"{time.Hours}h ")
                      .Append($"{time.Minutes}m ")
                      .Append($"{time.Seconds}s")
                      .ToString();
    }

    public static string ToTableView(this string timeSpanStr) =>
        TimeSpan.TryParse(timeSpanStr, out var timeSpan) ? timeSpan.ToToolTip() : string.Empty;
}