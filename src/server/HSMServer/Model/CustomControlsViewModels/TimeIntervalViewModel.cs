using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model
{
    public enum TimeInterval
    {
        [Display(Name = "Never")]
        None,
        [Display(Name = "10 minutes")]
        TenMinutes,
        [Display(Name = "20 minutes")]
        TwentyMinutes,
        [Display(Name = "30 minutes")]
        ThirtyMinutes,
        [Display(Name = "40 minutes")]
        FourtyMinutes,
        [Display(Name = "50 minutes")]
        FiftyMinutes,
        [Display(Name = "1 hour")]
        Hour,
        [Display(Name = "2 hours")]
        TwoHours,
        [Display(Name = "1 day")]
        Day,
        [Display(Name = "1 week")]
        Week,
        [Display(Name = "1 month")]
        Month,
        Custom,
    }


    public record TimeIntervalViewModel
    {
        public List<SelectListItem> IntervalItems { get; init; }

        public TimeInterval TimeInterval { get; set; }

        public string CustomTimeInterval { get; set; }


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
            var customPeriod = model?.CustomPeriod ?? 0;

            TimeInterval = SetTimeInterval(interval, customPeriod);
            CustomTimeInterval = new TimeSpan(customPeriod).ToString();
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
            if (TimeInterval == TimeInterval.Custom && TimeSpan.TryParse(CustomTimeInterval, out var timeInterval))
                return timeInterval.Ticks;

            return 0;
        }

        private static TimeInterval SetTimeInterval(CoreTimeInterval interval, long customIntervalTicks) =>
            interval switch
            {
                CoreTimeInterval.TenMinutes => TimeInterval.TenMinutes,
                CoreTimeInterval.Hour => TimeInterval.Hour,
                CoreTimeInterval.Day => TimeInterval.Day,
                CoreTimeInterval.Week => TimeInterval.Week,
                CoreTimeInterval.Month => TimeInterval.Month,
                CoreTimeInterval.Custom => customIntervalTicks == 0 ? TimeInterval.None : TimeInterval.Custom,
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
