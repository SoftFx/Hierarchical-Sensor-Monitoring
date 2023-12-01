using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model
{
    public enum TimeInterval : long
    {
        [Display(Name = "From parent")]
        FromParent = -10,
        Forever = -2,
        Custom = -1,

        [Display(Name = "Never")]
        None = 0L,
        [Display(Name = "1 minute")]
        OneMinute = 600_000_000,
        [Display(Name = "5 minutes")]
        FiveMinutes = 3_000_000_000,
        [Display(Name = "10 minutes")]
        TenMinutes = 6_000_000_000,
        [Display(Name = "30 minutes")]
        ThirtyMinutes = 18_000_000_000,

        [Display(Name = "1 hour")]
        Hour = 36_000_000_000,
        [Display(Name = "4 hours")]
        FourHours = 144_000_000_000,
        [Display(Name = "8 hours")]
        EightHours = 288_000_000_000,
        [Display(Name = "16 hours")]
        SixteenHours = 576_000_000_000,

        [Display(Name = "1 day")]
        Day = 864_000_000_000,
        [Display(Name = "1 day 12 hours")]
        ThirtySixHours = 1_296_000_000_000,
        [Display(Name = "2 days 12 hours")]
        SixtyHours = 2_160_000_000_000,
        [Display(Name = "1 week")]
        Week = 6_048_000_000_000,

        [Display(Name = "1 month")]
        Month = 26_784_000_000_000, // 31 days
        [Display(Name = "3 months")]
        ThreeMonths = 80_352_000_000_000, // 31 * 3
        [Display(Name = "6 months")]
        SixMonths = 160_704_000_000_000, // 31 * 6

        [Display(Name = "1 year")]
        Year = 315_360_000_000_000, //365 days
    }


    public static class PredefinedIntervals
    {
        public static HashSet<TimeInterval> ForFolderTimeout { get; } =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.None,
                TimeInterval.OneMinute,
                TimeInterval.FiveMinutes,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Day,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.Custom
            };

        public static HashSet<TimeInterval> ForTimeout { get; } =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.OneMinute,
                TimeInterval.FiveMinutes,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Day,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.Custom
            };

        public static HashSet<TimeInterval> ForRestore { get; } =
            new()
            {
                TimeInterval.OneMinute,
                TimeInterval.FiveMinutes,
                TimeInterval.TenMinutes,
                TimeInterval.Hour,
                TimeInterval.Custom
            };

        public static HashSet<TimeInterval> ForIgnore { get; } =
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

        public static HashSet<TimeInterval> ForKeepHistory { get; } =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.ThreeMonths,
                TimeInterval.SixMonths,
                TimeInterval.Year,
                TimeInterval.Forever,
                TimeInterval.Custom
            };

        public static HashSet<TimeInterval> ForSelfDestory { get; } =
            new()
            {
                TimeInterval.FromParent,
                TimeInterval.None,
                TimeInterval.Week,
                TimeInterval.Month,
                TimeInterval.ThreeMonths,
                TimeInterval.SixMonths,
                TimeInterval.Year,
                TimeInterval.Custom
            };
    }
}
