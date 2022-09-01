namespace HSMServer.Core.Model
{
    public sealed class Filter
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


        public Filter() { }

        internal Filter(Filter filter)
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
    }
}
