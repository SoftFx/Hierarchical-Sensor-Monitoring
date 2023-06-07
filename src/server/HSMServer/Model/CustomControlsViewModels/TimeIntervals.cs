using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model
{
    public enum TimeInterval
    {
        [Display(Name = "From parent")]
        FromParent,
        [Display(Name = "Never")]
        None,
        [Display(Name = "1 minute")]
        OneMinute,
        [Display(Name = "5 minutes")]
        FiveMinutes,
        [Display(Name = "10 minutes")]
        TenMinutes,
        [Display(Name = "30 minutes")]
        ThirtyMinutes,
        [Display(Name = "1 hour")]
        Hour,
        [Display(Name = "4 hours")]
        FourHours,
        [Display(Name = "8 hours")]
        EightHours,
        [Display(Name = "16 hours")]
        SixteenHours,
        [Display(Name = "1 day")]
        Day,
        [Display(Name = "1 day 12 hours")]
        ThirtySixHours,
        [Display(Name = "2 days 12 hours")]
        SixtyHours,
        [Display(Name = "1 week")]
        Week,
        [Display(Name = "1 month")]
        Month,
        Forever,
        Custom,
    }


    public static class PredefinedIntervals
    {
        public static List<TimeInterval> ForTimeout { get; } =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.None,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Day,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.Custom
            };

        public static List<TimeInterval> ForRestore { get; } =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.None,
                TimeInterval.OneMinute,
                TimeInterval.FiveMinutes,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Day,
                TimeInterval.Custom
            };

        public static List<TimeInterval> ForIgnore { get; } =
            new()
            {
                TimeInterval.FiveMinutes,
                TimeInterval.TenMinutes,
                TimeInterval.ThirtyMinutes,
                TimeInterval.FourHours,
                TimeInterval.EightHours,
                TimeInterval.SixteenHours,
                TimeInterval.ThirtySixHours,
                TimeInterval.SixtyHours,
                TimeInterval.Forever,
                TimeInterval.Custom
            };

        public static List<TimeInterval> ForCleanup { get; } =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.None,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.Custom
            };
    }
}
