using HSMServer.Model;
using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;

namespace HSMServer.Extensions
{
    public static class EnumExtensions
    {
        public static string GetDisplayName(this Enum enumValue)
        {
            var enumValueStr = enumValue.ToString();

            return enumValue.GetType()
                            .GetMember(enumValueStr)
                            .First()
                            .GetCustomAttribute<DisplayAttribute>()?.Name ?? enumValueStr;
        }


        public static bool IsParent(this TimeInterval interval) => interval == TimeInterval.FromParent;

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
                TimeInterval.Custom => Core.Model.TimeSpanValue.TryParse(customInterval, out var ticks) ? new DateTime(ticks) : time,
                _ => time,
            }).Ticks;
        }
    }
}
