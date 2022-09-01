using HSMServer.Core.Model;

namespace HSMServer.Model.ViewModel
{
    public class FilterViewModel
    {
        public bool HasOkStatus { get; set; }
        public bool HasWarningStatus { get; set; }
        public bool HasErrorStatus { get; set; }
        public bool HasUnknownStatus { get; set; }

        public bool SensorsHasData { get; set; }

        public bool HasTelegramNotifications { get; set; }
        public bool IsIgnoredSensors { get; set; }

        public bool IsBlockedSensors { get; set; }

        public int TreeUpdateInterval { get; set; } = 5;


        public FilterViewModel() { }

        public FilterViewModel(Filter filter)
        {
            HasOkStatus = filter.HasOkStatus;
            HasWarningStatus = filter.HasWarningStatus;
            HasErrorStatus = filter.HasErrorStatus;
            HasUnknownStatus = filter.HasUnknownStatus;
            SensorsHasData = filter.SensorsHasData;
            HasTelegramNotifications = filter.HasTelegramNotifications;
            IsIgnoredSensors = filter.IsIgnoredSensors;
            IsBlockedSensors = filter.IsBlockedSensors;
            TreeUpdateInterval = filter.TreeUpdateInterval;
        }


        public Filter ToFilter() =>
            new ()
            {
                HasOkStatus = HasOkStatus,
                HasWarningStatus = HasWarningStatus,
                HasErrorStatus = HasErrorStatus,
                HasUnknownStatus = HasUnknownStatus,
                SensorsHasData = SensorsHasData,
                HasTelegramNotifications = HasTelegramNotifications,
                IsIgnoredSensors = IsIgnoredSensors,
                IsBlockedSensors = IsBlockedSensors,
                TreeUpdateInterval = TreeUpdateInterval,
            };
    }
}
