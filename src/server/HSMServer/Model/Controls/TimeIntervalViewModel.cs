﻿using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Reflection;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model
{
    public record TimeIntervalViewModel
    {
        public const string CustomTemplate = "dd.HH:mm:ss";

        private static long _id = 0L;

        private readonly ParentRequest _parentRequest;

        private TimeInterval? _interval;
        private TimeSpan _customSpan;
        private string _customString;


        private bool HasParentValue => ParentValue is not null;

        private bool HasFolder => _parentRequest?.Invoke().IsFolder ?? false;


        internal delegate (TimeIntervalViewModel Value, bool IsFolder) ParentRequest();


        internal TimeIntervalViewModel ParentValue => _parentRequest?.Invoke().Value;


        public List<SelectListItem> IntervalItems { get; }

        public string Id { get; } = $"{_id++}";

        public bool UseCustomInputTemplate { get; } = true;

        public bool IsAlertBlock { get; init; }


        public string DisplayValue
        {
            get
            {
                var used = GetUsedValue(this);

                return TimeInterval.IsParent() ? TimeInterval.ToFromParentDisplay(used) : used;
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

                    if (val.IsUnset())
                        CustomSpan = TimeSpan.Zero;
                    else if (val.IsStatic() || val.IsDynamic())
                        CustomSpan = new TimeSpan((long)val);
                }
            }
        }

        internal TimeInterval TimeInterval => Interval ?? default;


        // public constructor without parameters for post actions
        public TimeIntervalViewModel() { }

        internal TimeIntervalViewModel(ParentRequest parentRequest)
        {
            _parentRequest = parentRequest;
        }

        internal TimeIntervalViewModel(HashSet<TimeInterval> intervals, bool useCustomTemplate = true)
        {
            IntervalItems = BuildSelectedList(intervals);
            UseCustomInputTemplate = useCustomTemplate;
        }

        internal TimeIntervalViewModel(HashSet<TimeInterval> intervals, ParentRequest request) : this(request)
        {
            IntervalItems = BuildSelectedList(intervals);

            if (!HasParentValue)
                IntervalItems.RemoveAt(0);
        }

        internal TimeIntervalViewModel(TimeIntervalViewModel model, HashSet<TimeInterval> intervals) : this(intervals, model._parentRequest)
        {
            Interval = model.TimeInterval;
            CustomSpan = model.CustomSpan;
        }

        internal TimeIntervalViewModel(TimeIntervalEntity entity, HashSet<TimeInterval> intervals) : this(intervals)
        {
            FromModel(new TimeIntervalModel(entity), intervals);

            if (!HasParentValue)
                IntervalItems.RemoveAt(0);
        }


        internal TimeIntervalModel ToModel(TimeIntervalViewModel current = null)
        {
            if (TimeInterval.IsStatic() || TimeInterval.IsCustom())
                return new TimeIntervalModel(CustomSpan.Ticks);
            else if (TimeInterval.IsUnset())
                return TimeIntervalModel.None;
            else if (TimeInterval.IsDynamic())
                return new TimeIntervalModel(TimeInterval.ToDynamicCore());

            // for saving view with TimeInterval = FromFolder
            var ticks = (current?.ParentValue ?? ParentValue)?.CustomSpan.Ticks ?? 0L;
            var hasFolder = current?.HasFolder ?? HasFolder;

            return new TimeIntervalModel(hasFolder ? CoreTimeInterval.FromFolder : CoreTimeInterval.FromParent, ticks);
        }

        internal TimeIntervalViewModel FromModel(TimeIntervalModel model, HashSet<TimeInterval> predefinedIntervals)
        {
            if (model.IsFromFolder)
                Interval = TimeInterval.FromParent;
            else if (!model.UseTicks) //dynamic to dynamic
            {
                Interval = model.Interval.ToDynamicServer();

                if (Interval is TimeInterval.None && predefinedIntervals.Contains(TimeInterval.Forever))
                    Interval = TimeInterval.Forever;
            }
            else if (TimeInterval.IsDefined(model.Ticks) && (predefinedIntervals?.Contains((TimeInterval)model.Ticks) ?? true)) //const ticks to enum
                Interval = (TimeInterval)model.Ticks;
            else
            {
                Interval = TimeInterval.Custom;
                CustomSpan = new TimeSpan(model.Ticks);
            }

            return this;
        }

        internal TimeIntervalEntity ToEntity() => ToModel().ToEntity();


        private List<SelectListItem> BuildSelectedList(HashSet<TimeInterval> intervals)
        {
            string KeyBuilder(TimeInterval interval) => interval.IsParent() ? interval.ToFromParentDisplay(GetUsedValue(ParentValue)) : interval.GetDisplayName();

            return intervals.ToSelectedItems(KeyBuilder);
        }

        private static string GetUsedValue(TimeIntervalViewModel model) =>
            model?.Interval switch
            {
                TimeInterval.Custom => model.CustomSpan.ToReadableView(),
                TimeInterval.FromParent => GetUsedValue(model.ParentValue),
                _ => model?.TimeInterval.GetDisplayName()
            };
    }
}