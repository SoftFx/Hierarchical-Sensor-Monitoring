using System;
using System.Text;

namespace HSMDataCollector.Extensions
{
    public static class TimeSpanExtensions
    {
        private const string DefaultEmptyTime = "0 seconds";


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
            var ans = tooltip.Append(BuildUnit("day", time.Days))
                             .Append(BuildUnit("hour", time.Hours))
                             .Append(BuildUnit("minute", time.Minutes))
                             .Append(BuildUnit("second", time.Seconds))
                             .ToString();

            return string.IsNullOrEmpty(ans) ? DefaultEmptyTime : ans;
        }


        public static DateTime Ceil(this DateTime time, TimeSpan span)
        {
            var roundTicks = span.Ticks;

            return roundTicks == 0 ? time : new DateTime(time.Ticks / roundTicks * roundTicks + roundTicks, DateTimeKind.Utc);
        }
    }
}