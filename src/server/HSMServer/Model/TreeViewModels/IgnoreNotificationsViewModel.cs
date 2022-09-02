using HSMCommon.Constants;
using HSMServer.Model.TreeViewModels;
using System;
using System.Collections.Generic;

namespace HSMServer.Model
{
    public class IgnoreNotificationsViewModel
    {
        public string Path { get; }

        public string TreeElement { get; }

        public string EncodedId { get; set; }

        public TimeIntervalViewModel IgnorePeriod { get; set; }

        public int Days { get; set; }

        public int Hours { get; set; }

        public int Minutes { get; set; }

        public DateTime EndOfIgnorePeriod
        {
            get
            {
                var now = DateTime.UtcNow;

                return now.AddDays(Days)
                          .AddHours(Hours)
                          .AddMinutes(Minutes)
                          .AddSeconds(-now.Second);
            }
        }


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
        }
    }
}
