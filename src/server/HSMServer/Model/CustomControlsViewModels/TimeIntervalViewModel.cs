using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model
{
    public enum TimeInterval
    {
        [Display(Name = "Never")]
        None,
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


    public record TimeIntervalViewModel
    {
        public const string CustomTemplate = "dd.HH:mm:ss";


        public List<SelectListItem> IntervalItems { get; }

        public bool CanCustomInputBeVisible { get; init; } = true;

        public TimeInterval TimeInterval { get; set; }

        public string CustomTimeInterval { get; set; }

        public string DisplayInterval => TimeInterval == TimeInterval.Custom
            ? CustomTimeInterval
            : TimeInterval.GetDisplayName();


        // public constructor without parameters for post actions
        public TimeIntervalViewModel() { }

        internal TimeIntervalViewModel(List<TimeInterval> intervals)
        {
            IntervalItems = GetIntrevalItems(intervals);
        }

        internal TimeIntervalViewModel(TimeIntervalModel model, List<TimeInterval> intervals) : this(intervals)
        {
            Update(model);
        }


        internal void Update(TimeIntervalModel model)
        {
            var interval = model?.TimeInterval ?? CoreTimeInterval.Custom;
            var customPeriod = model?.CustomPeriod ?? 0L;

            TimeInterval = SetTimeInterval(interval, customPeriod);
            CustomTimeInterval = TimeSpanValue.TicksToString(customPeriod);
        }

        internal TimeIntervalModel ToModel() =>
            new()
            {
                TimeInterval = GetIntervalOption(),
                CustomPeriod = GetCustomIntervalTicks(),
            };

        private CoreTimeInterval GetIntervalOption() =>
            TimeInterval switch
            {
                TimeInterval.TenMinutes => CoreTimeInterval.TenMinutes,
                TimeInterval.Hour => CoreTimeInterval.Hour,
                TimeInterval.Day => CoreTimeInterval.Day,
                TimeInterval.Week => CoreTimeInterval.Week,
                TimeInterval.Month => CoreTimeInterval.Month,
                TimeInterval.Custom => CoreTimeInterval.Custom,
                _ => CoreTimeInterval.Custom,
            };

        private long GetCustomIntervalTicks()
        {
            if (TimeInterval == TimeInterval.Custom && TimeSpanValue.TryParse(CustomTimeInterval, out var ticks))
                return ticks;
            
            return 0L;
        }

        private static TimeInterval SetTimeInterval(CoreTimeInterval interval, long customIntervalTicks) =>
            interval switch
            {
                CoreTimeInterval.TenMinutes => TimeInterval.TenMinutes,
                CoreTimeInterval.Hour => TimeInterval.Hour,
                CoreTimeInterval.Day => TimeInterval.Day,
                CoreTimeInterval.Week => TimeInterval.Week,
                CoreTimeInterval.Month => TimeInterval.Month,
                CoreTimeInterval.Custom => customIntervalTicks == 0L ? TimeInterval.None : TimeInterval.Custom,
                _ => TimeInterval.None,
            };

        private static List<SelectListItem> GetIntrevalItems(List<TimeInterval> intervals)
        {
            var items = new List<SelectListItem>(intervals.Count);

            foreach (var interval in intervals)
                items.Add(new SelectListItem() { Text = interval.GetDisplayName(), Value = interval.ToString() });

            return items;
        }
    }
}