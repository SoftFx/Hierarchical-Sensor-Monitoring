using HSMCommon.Constants;
using HSMServer.Model.TreeViewModels;
using System;
using System.Collections.Generic;

namespace HSMServer.Model
{
    public class IgnoreNotificationsViewModel
    {
        private static readonly List<TimeInterval> _predefinedIntervals =
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
                TimeInterval.Custom
            };


        public string Path { get; }

        public string TreeElement { get; }

        public string EncodedId { get; set; }

        public TimeIntervalViewModel IgnorePeriod { get; set; }

        public int Days { get; set; }

        public int Hours { get; set; }

        public int Minutes { get; set; }

        public DateTime DateTimeNow { get; set; }

        public DateTime EndOfIgnorePeriod => DateTimeNow.AddDays(Days)
                                                        .AddHours(Hours)
                                                        .AddMinutes(Minutes);


        // public constructor without parameters for action Home/IgnoreNotifications
        public IgnoreNotificationsViewModel() { }

        public IgnoreNotificationsViewModel(SensorNodeViewModel sensor) : this(sensor.EncodedId)
        {
            Path = $"{sensor.Product}{CommonConstants.SensorPathSeparator}{sensor.Path}";
            TreeElement = nameof(sensor);
        }

        public IgnoreNotificationsViewModel(ProductNodeViewModel node) : this(node.EncodedId)
        {
            var nodePathParts = new List<string>() { node.Name };
            NodeViewModel parent = node.Parent;

            while (parent != null)
            {
                nodePathParts.Add(parent.Name);
                parent = parent.Parent;
            }

            nodePathParts.Reverse();

            Path = string.Join(CommonConstants.SensorPathSeparator, nodePathParts);
            TreeElement = nameof(node);
        }

        private IgnoreNotificationsViewModel(string encodedId)
        {
            EncodedId = encodedId;
            IgnorePeriod = new(_predefinedIntervals)
            {
                CanCustomInputBeVisible = false,
            };

            var now = DateTime.UtcNow;
            DateTimeNow = now.AddSeconds(-now.Second)
                             .AddMilliseconds(-now.Millisecond);
        }
    }
}
