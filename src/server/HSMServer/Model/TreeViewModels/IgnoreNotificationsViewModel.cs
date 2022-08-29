using HSMServer.Model.TreeViewModels;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model
{
    public class IgnoreNotificationsViewModel
    {
        public string SensorPath { get; }

        public string EncodedId { get; set; }

        [Display(Name = "Ignore period")]
        public TimeIntervalViewModel IgnorePeriod { get; set; }


        // public constructor without parameters for action Home/IgnoreNotifications
        public IgnoreNotificationsViewModel() { }

        public IgnoreNotificationsViewModel(SensorNodeViewModel sensor)
        {
            SensorPath = $"{sensor.Product}/{sensor.Path}";
            EncodedId = sensor.EncodedId;
        }
    }
}
