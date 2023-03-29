using System;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model
{
    public enum TimeInterval
    {
        [Display(Name = "From parent")]
        FromParent,
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
        
        
        private readonly Func<TimeIntervalViewModel> _getParentInterval;


        private bool HasIntervalValue => _getParentInterval?.Invoke() is null;


        private static long _id = 0L;


        public List<SelectListItem> IntervalItems { get; }

        public string Id { get; } = $"{_id++}";

        public bool CustomItemIsVisible { get; init; } = true;

        public TimeInterval TimeInterval { get; set; }
        
        public string CustomTimeInterval { get; set; }

        public string DisplayInterval => TimeInterval.IsCustom() switch
        {
            true => CustomTimeInterval,
            false => HasIntervalValue ? _getParentInterval?.Invoke().DisplayInterval : TimeInterval.GetDisplayName()
        };

        
        // public constructor without parameters for post actions
        public TimeIntervalViewModel() { }

        internal TimeIntervalViewModel(List<TimeInterval> intervals)
        {
            IntervalItems = GetIntrevalItems(intervals);
        }

        internal TimeIntervalViewModel(TimeIntervalModel model, List<TimeInterval> intervals, Func<TimeIntervalViewModel> getParentInterval) : this(intervals)
        {
            _getParentInterval = getParentInterval;
            if (!HasIntervalValue)
                IntervalItems.RemoveAt(0);

            Update(model);
        }


        internal void Update(TimeIntervalModel model)
        {
            var interval = model?.TimeInterval ?? CoreTimeInterval.FromParent;
            var customPeriod = model?.CustomPeriod ?? 0L;

            TimeInterval = SetTimeInterval(interval, customPeriod);
            CustomTimeInterval = TimeSpanValue.TicksToString(customPeriod);
        }

        internal TimeIntervalModel ToModel() => new(GetIntervalOption(), GetCustomIntervalTicks());


        private CoreTimeInterval GetIntervalOption() =>
            TimeInterval switch
            {
                TimeInterval.TenMinutes => CoreTimeInterval.TenMinutes,
                TimeInterval.Hour => CoreTimeInterval.Hour,
                TimeInterval.Day => CoreTimeInterval.Day,
                TimeInterval.Week => CoreTimeInterval.Week,
                TimeInterval.Month => CoreTimeInterval.Month,
                TimeInterval.FromParent => CoreTimeInterval.FromParent,
                _ => CoreTimeInterval.Custom,
            };

        private long GetCustomIntervalTicks()
        {
            return TimeInterval.IsCustom() && TimeSpanValue.TryParse(CustomTimeInterval, out var ticks) ? ticks : 0L;
        }


        private static TimeInterval SetTimeInterval(CoreTimeInterval interval, long ticks) =>
            interval switch
            {
                CoreTimeInterval.TenMinutes => TimeInterval.TenMinutes,
                CoreTimeInterval.Hour => TimeInterval.Hour,
                CoreTimeInterval.Day => TimeInterval.Day,
                CoreTimeInterval.Week => TimeInterval.Week,
                CoreTimeInterval.Month => TimeInterval.Month,
                CoreTimeInterval.FromParent => TimeInterval.FromParent,
                CoreTimeInterval.Custom => ticks == 0L ? TimeInterval.None : TimeInterval.Custom,
                _ => TimeInterval.None,
            };

        private static List<SelectListItem> GetIntrevalItems(List<TimeInterval> intervals)
        {
            return intervals.Select(u => new SelectListItem(u.GetDisplayName(), $"{u}")).ToList();
        }
    }
}