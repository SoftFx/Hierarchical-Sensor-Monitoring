using HSMServer.Model;
using System;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Extensions
{
    public static class TimeIntervalExtensions
    {
        public static bool IsParent(this TimeInterval interval) => interval == TimeInterval.FromParent;

        public static bool IsCustom(this TimeInterval interval) => interval == TimeInterval.Custom;

        public static long ToCustomTicks(this TimeInterval interval, string customInterval)
        {
            var time = DateTime.MinValue;

            return (interval switch
            {
                TimeInterval.OneMinute => time.AddMinutes(1),
                TimeInterval.FiveMinutes => time.AddMinutes(5),
                TimeInterval.TenMinutes => time.AddMinutes(10),
                TimeInterval.ThirtyMinutes => time.AddMinutes(30),
                TimeInterval.Hour => time.AddHours(1),
                TimeInterval.FourHours => time.AddHours(4),
                TimeInterval.EightHours => time.AddHours(8),
                TimeInterval.SixteenHours => time.AddHours(16),
                TimeInterval.Day => time.AddDays(1),
                TimeInterval.ThirtySixHours => time.AddHours(36),
                TimeInterval.SixtyHours => time.AddHours(60),
                TimeInterval.Week => time.AddDays(7),
                TimeInterval.Month => time.AddMonths(1),
                TimeInterval.Custom => customInterval.TryParse(out var ticks) ? new DateTime(ticks) : time,
                _ => time,
            }).Ticks;
        }

        public static TimeInterval ToServer(this CoreTimeInterval interval, long ticks) =>
            interval switch
            {
                CoreTimeInterval.OneMinute => TimeInterval.OneMinute,
                CoreTimeInterval.FiveMinutes => TimeInterval.FiveMinutes,
                CoreTimeInterval.TenMinutes => TimeInterval.TenMinutes,
                CoreTimeInterval.Hour => TimeInterval.Hour,
                CoreTimeInterval.Day => TimeInterval.Day,
                CoreTimeInterval.Week => TimeInterval.Week,
                CoreTimeInterval.Month => TimeInterval.Month,
                CoreTimeInterval.FromFolder or CoreTimeInterval.FromParent => TimeInterval.FromParent,
                CoreTimeInterval.Custom => ticks == 0L ? TimeInterval.None : TimeInterval.Custom,
                _ => TimeInterval.None,
            };

        public static CoreTimeInterval ToCore(this TimeInterval interval, bool parentIsFolder = false) =>
            interval switch
            {
                TimeInterval.OneMinute => CoreTimeInterval.OneMinute,
                TimeInterval.FiveMinutes => CoreTimeInterval.FiveMinutes,
                TimeInterval.TenMinutes => CoreTimeInterval.TenMinutes,
                TimeInterval.Hour => CoreTimeInterval.Hour,
                TimeInterval.Day => CoreTimeInterval.Day,
                TimeInterval.Week => CoreTimeInterval.Week,
                TimeInterval.Month => CoreTimeInterval.Month,
                TimeInterval.FromParent => parentIsFolder ? CoreTimeInterval.FromFolder : CoreTimeInterval.FromParent,
                _ => CoreTimeInterval.Custom,
            };
    }
}
