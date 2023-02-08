using HSMServer.Model.TreeViewModels;
using System;
using System.Collections.Generic;

namespace HSMServer.Model
{
    public enum NotificationsTarget
    {
        Groups,
        Accounts
    }
    
    public class IgnoreNotificationsViewModel
    {
        private const string NodeTreeElement = "node";
        private const string SensorTreeElement = "sensor";

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

        public NotificationsTarget NotificationsTarget { get; set; }
        
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

        public IgnoreNotificationsViewModel(NodeViewModel node)
        {
            EncodedId = node.EncodedId;
            Path = $"{node.Product}{node.Path}";
            TreeElement = node is SensorNodeViewModel ? SensorTreeElement : NodeTreeElement;
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
