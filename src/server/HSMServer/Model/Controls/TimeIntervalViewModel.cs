using HSMCommon.Extensions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.Controls;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Reflection;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;

namespace HSMServer.Model
{
    public sealed record TimeIntervalViewModel : SensorSettingControlBase<TimeIntervalViewModel>
    {
        public const string CustomTemplate = "dd.HH:mm:ss";

        private static long _id = 0L;

        private readonly string _noneIntervalDisplayName = TimeInterval.None.GetDisplayName();

        private TimeInterval? _interval;
        private TimeSpan _customSpan;
        private string _customString;

        // A node setting with nothing to inherit from (a folder, a product in no folder) stored as FromParent: it never
        // expires. Its list has no "from parent" item, so it is shown as the item that behaves the same (Forever, or
        // None where the list has no Forever), and while that item stays selected the stored model is what is saved:
        // an unchanged form neither changes the setting nor records a change (#1945). Null for every other view.
        private TimeIntervalModel _storedFromParent;


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

        internal bool IsIntervalNone => TimeInterval is TimeInterval.None ||
                                        (TimeInterval.IsParent() && GetUsedValue(this) == _noneIntervalDisplayName);


        // public constructor without parameters for post actions
        public TimeIntervalViewModel() : base() { }

        internal TimeIntervalViewModel(ParentRequest parentRequest) : base(parentRequest) { }

        internal TimeIntervalViewModel(HashSet<TimeInterval> intervals, bool useCustomTemplate = true)
        {
            IntervalItems = BuildSelectedList(intervals);
            UseCustomInputTemplate = useCustomTemplate;
        }

        internal TimeIntervalViewModel(HashSet<TimeInterval> intervals, ParentRequest request) : this(request)
        {
            IntervalItems = BuildSelectedList(intervals);

            if (!HasParent)
                IntervalItems.RemoveAt(0);
        }

        internal TimeIntervalViewModel(TimeIntervalViewModel model, HashSet<TimeInterval> intervals) : this(intervals, model._parentRequest)
        {
            Interval = model.TimeInterval;
            CustomSpan = model.CustomSpan;
        }

        internal TimeIntervalViewModel(HashSet<TimeInterval> intervals, TimeIntervalViewModel model)
        {
            Interval = model.TimeInterval;
            CustomSpan = model.CustomSpan;

            IntervalItems = BuildSelectedList(intervals);
        }


        internal TimeIntervalViewModel(TimeIntervalEntity entity, HashSet<TimeInterval> intervals) : this(intervals)
        {
            FromNodeSetting(new TimeIntervalModel(entity), intervals);

            if (!HasParent)
                IntervalItems.RemoveAt(0);
        }


        internal TimeIntervalModel ToModel(TimeIntervalViewModel current = null)
        {
            var stored = current ?? this;

            if (stored._storedFromParent is { } fromParent && TimeInterval == stored.TimeInterval)
                return fromParent;

            if (TimeInterval.IsStatic() || TimeInterval.IsCustom())
                return new TimeIntervalModel(CustomSpan.Ticks);
            else if (TimeInterval.IsUnset())
                return TimeIntervalModel.None;
            else if (TimeInterval.IsDynamic())
                return new TimeIntervalModel(TimeInterval.ToDynamicCore());

            // for saving view with TimeInterval = FromFolder
            var parent = current?.Parent ?? Parent;
            var hasFolder = current?.HasFolder ?? HasFolder;

            // A folder's calendar preset is inherited as itself, not as its nominal length in ticks (#1864). Whether the
            // folder says a calendar preset is read from its model, the value it stores, never from the view's interval.
            if (hasFolder && parent is not null)
            {
                var folderModel = parent.ToModel();

                if (TimeIntervalModel.IsCalendarPreset(folderModel.Interval))
                    return folderModel.ToFromFolderModel();
            }

            var ticks = parent?.CustomSpan.Ticks ?? 0L;

            return new TimeIntervalModel(hasFolder ? CoreTimeInterval.FromFolder : CoreTimeInterval.FromParent, ticks);
        }

        /// <summary>
        /// Reads a node's own setting (a folder's, or a product's or sensor's <c>CurValue</c>): as <see cref="FromModel"/>,
        /// except that FromParent on a view with no parent is shown as the item that never expires and kept as stored
        /// while that item stays selected (see <see cref="_storedFromParent"/>).
        /// </summary>
        internal TimeIntervalViewModel FromNodeSetting(TimeIntervalModel model, HashSet<TimeInterval> predefinedIntervals)
        {
            FromModel(model, predefinedIntervals);

            if (model.IsFromParent && !HasParent)
            {
                Interval = predefinedIntervals?.Contains(TimeInterval.Forever) == true ? TimeInterval.Forever : TimeInterval.None;
                _storedFromParent = model;
            }

            return this;
        }

        /// <summary>True when <paramref name="posted"/> keeps the item this view shows for a stored FromParent.</summary>
        internal bool KeepsStoredFromParent(TimeIntervalViewModel posted) =>
            _storedFromParent is not null && posted is not null && posted.TimeInterval == TimeInterval;

        internal TimeIntervalViewModel FromModel(TimeIntervalModel model, HashSet<TimeInterval> predefinedIntervals)
        {
            _storedFromParent = null;

            if (model.IsFromFolder)
                Interval = TimeInterval.FromParent;
            else if (!model.UseTicks) //dynamic to dynamic
            {
                Interval = model.Interval.ToDynamicServer();

                if (Interval is TimeInterval.None && predefinedIntervals.Contains(TimeInterval.Forever))
                    Interval = TimeInterval.Forever;
            }
            // const ticks to enum, the fixed periods only: ticks equal to a calendar preset's nominal length (31, 93, 186,
            // 365, 1826 days) are a custom period: read as the preset, they would be saved back as the calendar preset
            // (#1864; 1826 days, the collectors' history of version sensors, #1945)
            else if (TimeInterval.IsDefined(model.Ticks) && !((TimeInterval)model.Ticks).IsDynamic() &&
                     (predefinedIntervals?.Contains((TimeInterval)model.Ticks) ?? true))
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
            string KeyBuilder(TimeInterval interval) => interval.IsParent() ? interval.ToFromParentDisplay(GetUsedValue(Parent)) : interval.GetDisplayName();

            return intervals.ToSelectedItems(KeyBuilder);
        }

        private static string GetUsedValue(TimeIntervalViewModel model) =>
            model?.Interval switch
            {
                TimeInterval.Custom => model.CustomSpan.ToReadableView(),
                TimeInterval.FromParent => GetUsedValue(model.Parent),
                _ => model?.TimeInterval.GetDisplayName()
            };
    }
}