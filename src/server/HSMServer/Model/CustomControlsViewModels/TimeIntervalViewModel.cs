using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
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


    public record TimeIntervalViewModel
    {
        public const string CustomTemplate = "dd.HH:mm:ss";


        private readonly Func<TimeIntervalViewModel> _getParentValue;

        private static long _id = 0L;


        public List<SelectListItem> IntervalItems { get; }

        public string Id { get; } = $"{_id++}";

        public bool UseCustomInputTemplate { get; }


        private bool HasIntervalValue => _getParentValue?.Invoke() is not null;

        private string UsedInterval => TimeInterval switch
        {
            TimeInterval.Custom => CustomTimeInterval.ToTableView(),
            TimeInterval.FromParent => HasIntervalValue ? _getParentValue?.Invoke().UsedInterval : TimeInterval.GetDisplayName(),
            _ => TimeInterval.GetDisplayName()
        };

        public string DisplayInterval => TimeInterval.IsParent() ? $"From parent ({UsedInterval})" : UsedInterval;


        public TimeInterval TimeInterval { get; set; }

        public string CustomTimeInterval { get; set; }


        // public constructor without parameters for post actions
        public TimeIntervalViewModel() { }

        internal TimeIntervalViewModel(TimeIntervalModel model, Func<TimeIntervalViewModel> getParentValue)
        {
            _getParentValue = getParentValue;

            Update(model);
        }

        internal TimeIntervalViewModel(List<TimeInterval> intervals, bool useCutomTemplate = true)
        {
            IntervalItems = GetIntrevalItems(intervals);
            UseCustomInputTemplate = useCutomTemplate;
        }

        internal TimeIntervalViewModel(TimeIntervalViewModel model, List<TimeInterval> intervals) : this(intervals)
        {
            _getParentValue = model._getParentValue;

            TimeInterval = model.TimeInterval;
            CustomTimeInterval = model.CustomTimeInterval;

            if (!HasIntervalValue)
                IntervalItems.RemoveAt(0);
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
                TimeInterval.OneMinute => CoreTimeInterval.OneMinute,
                TimeInterval.FiveMinutes => CoreTimeInterval.FiveMinutes,
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
                CoreTimeInterval.OneMinute => TimeInterval.OneMinute,
                CoreTimeInterval.FiveMinutes => TimeInterval.FiveMinutes,
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