using System;
using System.Text;

namespace HSMCommon.Extensions
{
    public static class TimeSpanExtensions
    {
        public static string ToReadableView(this string str) =>
            TimeSpan.TryParse(str, out var time) ? time.ToReadableView() : str;

        public static string ToReadableView(this TimeSpan time)
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
            TimeSpan.TryParse(timeSpanStr, out var timeSpan) ? timeSpan.ToReadableView() : string.Empty;

        public static string TicksToString(this long ticks)
        {
            var timeSpan = TimeSpan.FromTicks(ticks);
            return $"{timeSpan.Days}.{timeSpan.Hours}:{timeSpan.Minutes}:{timeSpan.Seconds}";
        }

        public static DateTime Ceil(this DateTime time, TimeSpan span)
        {
            var roundTicks = span.Ticks;

            return roundTicks == 0 ? time : new DateTime(time.Ticks / roundTicks * roundTicks + roundTicks, DateTimeKind.Utc);
        }

        public static DateTime Floor(this DateTime time, TimeSpan span)
        {
            var roundTicks = span.Ticks;

            return new DateTime(time.Ticks / roundTicks * roundTicks, DateTimeKind.Utc);
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
}
