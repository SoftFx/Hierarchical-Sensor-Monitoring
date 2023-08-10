﻿using System;

namespace HSMServer.Extensions
{
    public static class DateTimeExtensions
    {
        private const string DateTimeDefaultFormat = "dd/MM/yyyy HH:mm:ss";


        public static string ToDefaultFormat(this DateTime dateTime) => dateTime.ToString(DateTimeDefaultFormat);

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

        internal static DateTime Ceil(this DateTime time, TimeSpan span)
        {
            var roundTicks = span.Ticks;

            return roundTicks == 0 ? time : new DateTime(time.Ticks / roundTicks * roundTicks + roundTicks);
        }

        internal static DateTime Floor(this DateTime time, TimeSpan span)
        {
            var roundTicks = span.Ticks;

            return new DateTime(time.Ticks / roundTicks * roundTicks);
        }
    }
}