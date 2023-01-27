using System;
using System.Text;
using Org.BouncyCastle.Asn1.Pkcs;

namespace HSMServer.Extensions;

public static class TimeSpanExtensions
{
    public static string ToToolTip(this TimeSpan configurationExpiredTime)
    {
        var tooltip = new StringBuilder(1 << 4);

        if (configurationExpiredTime.Days != 0) tooltip.Append($"{configurationExpiredTime.Days}d ");
        tooltip.Append($"{configurationExpiredTime.Hours}h ");
        tooltip.Append($"{configurationExpiredTime.Minutes}m ");
        tooltip.Append($"{configurationExpiredTime.Seconds}s");
            
        return tooltip.ToString();
    }

    public static string ToFormattedString(this string timeSpanRepresentation)
    {
        var time = timeSpanRepresentation.Split(':');
        var daysTime = time[0].Split('.');
        int days = 0;
        int hours;
        int minutes;
        int seconds;
        if (daysTime.Length > 1)
        {
            int.TryParse(daysTime[0], out days);
            int.TryParse(daysTime[1], out hours);
        }
        else
        {
            int.TryParse(time[0], out hours);
        }
        int.TryParse(time[1], out minutes);
        int.TryParse(time[2], out seconds);

        if (days > 0)
            return $"{days}d {hours}h {minutes}m {seconds}s";
        return $"{hours}h {minutes}m {seconds}s";
    }
}