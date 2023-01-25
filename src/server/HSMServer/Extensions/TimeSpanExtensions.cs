using System;
using System.Text;

namespace HSMServer.Extensions;

public static class TimeSpanExtensions
{
    public static string ToToolTip(this TimeSpan configurationExpiredTime)
    {
        var tooltip = new StringBuilder();

        if (configurationExpiredTime.Days != 0) tooltip.Append($"{configurationExpiredTime.Days}d ");
        tooltip.Append($"{configurationExpiredTime.Hours}h ");
        tooltip.Append($"{configurationExpiredTime.Minutes}m ");
        tooltip.Append($"{configurationExpiredTime.Seconds}s");
            
        return tooltip.ToString();
    }
}