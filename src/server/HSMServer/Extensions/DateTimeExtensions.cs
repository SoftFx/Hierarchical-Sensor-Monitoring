using System;
using System.Globalization;

namespace HSMServer.Extensions
{
    public static class DateTimeExtensions
    {
        private const string DateTimeDefaultFormat = "dd/MM/yyyy HH:mm:ss";
        private const string DateTimeWindowsFormat = "dd.MM.yyyyTHH.mm";


        public static string ToDefaultFormat(this DateTime dateTime) => dateTime.ToString(DateTimeDefaultFormat);

        public static string ToDefaultFormat(this DateTime dateTime, string minValueString) =>
            dateTime == DateTime.MinValue ? minValueString : dateTime.ToDefaultFormat();

        public static DateTime ParseFromDefault(this string str) =>
            DateTime.TryParseExact(str, DateTimeDefaultFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var time) ? time : DateTime.MinValue;

        public static string ToWindowsFormat(this DateTime dateTime) => dateTime.ToString(DateTimeWindowsFormat);


        public static string GetTimeAgo(this DateTime lastUpdateDate)
        {
            string UnitsToString(double value, string unit)
            {
                int intValue = Convert.ToInt32(value);
                return intValue > 1 ? $"{intValue} {unit}s" : $"1 {unit}";
            }

            var time = lastUpdateDate != DateTime.MinValue ? DateTime.UtcNow - lastUpdateDate : TimeSpan.MinValue;

            if (time == TimeSpan.MinValue)
                return " - no data";

            if (time.TotalDays > 30)
                return "> a month ago";

            if (time.TotalDays >= 1)
                return $"> {UnitsToString(time.TotalDays, "day")} ago";

            if (time.TotalHours >= 1)
                return $"> {UnitsToString(time.TotalHours, "hour")} ago";

            if (time.TotalMinutes >= 1)
                return $"{UnitsToString(time.TotalMinutes, "minute")} ago";

            return time.TotalSeconds < 60 ? "< 1 minute ago" : "no info";
        }

        internal static DateTime ToUtcKind(this DateTime dateTime) => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);

        internal static DateTime RoundToMin(this DateTime time) => time.AddSeconds(-time.Second).AddMicroseconds(-time.Millisecond);

        internal static long ToUnixMilliseconds(this DateTime time) => new DateTimeOffset(time).ToUnixTimeMilliseconds();
    }
}