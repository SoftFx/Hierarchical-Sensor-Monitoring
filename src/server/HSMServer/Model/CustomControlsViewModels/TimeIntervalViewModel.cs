using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model
{
    public record TimeIntervalViewModel
    {
        public const string CustomTemplate = "dd.HH:mm:ss";

        private static long _id = 0L;

        private readonly Func<(TimeIntervalViewModel Value, bool IsFolder)> _getParentValue;
        private TimeInterval? _interval;
        private TimeSpan _customSpan;
        private string _customString;


        private bool HasParentValue => _getParentValue?.Invoke().Value is not null;

        private bool HasFolder => _getParentValue?.Invoke().IsFolder ?? false;


        public List<SelectListItem> IntervalItems { get; }

        public string Id { get; } = $"{_id++}";

        public bool UseCustomInputTemplate { get; }

        public bool IsAlertBlock { get; init; }


        public string DisplayValue
        {
            get
            {
                static string GetUsedValue(TimeIntervalViewModel model) => model?.Interval switch
                {
                    TimeInterval.Custom => model.CustomSpan.ToTableView(),
                    TimeInterval.FromParent => GetUsedValue(model._getParentValue?.Invoke().Value),
                    _ => model?.TimeInterval.GetDisplayName()
                };

                var used = GetUsedValue(this);

                return TimeInterval.IsParent() ? $"From parent ({used})" : used;
            }
        }

        public TimeSpan CustomSpan
        {
            get => _customSpan;
            set
            {
                _customSpan = value;
                _customString = value.ToString();
            }
        }

        public string CustomString
        {
            get => _customString;
            set
            {
                _customString = value;
                _customSpan = value is null ? TimeSpan.Zero : TimeSpan.Parse(value);
            }
        }

        public TimeInterval? Interval
        {
            get => _interval;
            set
            {
                _interval = value;

                if (_interval.HasValue)
                {
                    var val = _interval.Value;

                    if (val.IsUnset() || val.IsStatic() || val.IsDynamic())
                        CustomSpan = new TimeSpan((long)val);
                }
            }
        }

        internal TimeInterval TimeInterval => Interval ?? default;


        // public constructor without parameters for post actions
        public TimeIntervalViewModel() { }

        internal TimeIntervalViewModel(Func<(TimeIntervalViewModel, bool)> getParentValue)
        {
            _getParentValue = getParentValue;
        }

        internal TimeIntervalViewModel(List<TimeInterval> intervals, bool useCustomTemplate = true)
        {
            IntervalItems = intervals.ToSelectedItems(k => k.GetDisplayName());
            UseCustomInputTemplate = useCustomTemplate;
        }

        internal TimeIntervalViewModel(TimeIntervalViewModel model, List<TimeInterval> intervals) : this(intervals)
        {
            _getParentValue = model._getParentValue;

            Interval = model.Interval;
            CustomSpan = model.CustomSpan;

            if (!HasParentValue)
                IntervalItems.RemoveAt(0);
        }

        internal TimeIntervalViewModel(TimeIntervalEntity entity, List<TimeInterval> intervals) : this(intervals)
        {
            FromModel(new TimeIntervalModel(entity));
        }


        internal TimeIntervalModel ToModel()
        {
            if (TimeInterval.IsStatic() || TimeInterval.IsCustom())
                return new TimeIntervalModel(CustomSpan.Ticks);
            else if (TimeInterval.IsUnset())
                return TimeIntervalModel.None;
            else if (TimeInterval.IsDynamic())
                return new TimeIntervalModel(TimeInterval.ToDynamicCore());

            var ticks = _getParentValue?.Invoke().Value.CustomSpan.Ticks ?? 0L;

            return new TimeIntervalModel(HasFolder ? CoreTimeInterval.FromFolder : CoreTimeInterval.FromParent, ticks);
        }

        internal TimeIntervalViewModel FromModel(TimeIntervalModel model)
        {
            if (model.IsFromFolder)
                Interval = TimeInterval.FromParent;
            else if (!model.UseTicks) //dynamic to dynamic
                Interval = model.Interval.ToDynamicServer();
            else if (TimeInterval.IsDefined(model.Ticks)) //const ticks to enum
                Interval = (TimeInterval)model.Ticks;
            else
            {
                Interval = TimeInterval.Custom;
                CustomSpan = new TimeSpan(model.Ticks);
            }

            return this;
        }

        internal TimeIntervalEntity ToEntity() => ToModel().ToEntity();
    }
}