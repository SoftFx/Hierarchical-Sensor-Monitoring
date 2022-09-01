using HSMServer.Model.TreeViewModels;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model
{
    public class IgnoreNotificationsViewModel
    {
        public string SensorPath { get; }

        public string EncodedId { get; set; }

        public TimeIntervalViewModel IgnorePeriod { get; set; }

        public int Days { get; set; }

        public int Hours { get; set; }

        public int Minutes { get; set; }


        // public constructor without parameters for action Home/IgnoreNotifications
        public IgnoreNotificationsViewModel() { }

        public IgnoreNotificationsViewModel(SensorNodeViewModel sensor)
        {
            SensorPath = $"{sensor.Product}/{sensor.Path}";
            EncodedId = sensor.EncodedId;
        }
    }
}
