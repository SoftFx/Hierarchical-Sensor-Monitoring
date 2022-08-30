using HSMDatabase.AccessManager.DatabaseEntities;

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

        public int TreeUpdateInterval { get; set; }


        internal Filter() { }

        internal Filter(FilterEntity entity)
        {
            HasOkStatus = entity.HasOkStatus;
            HasWarningStatus = entity.HasWarningStatus;
            HasErrorStatus = entity.HasErrorStatus;
            HasUnknownStatus = entity.HasUnknownStatus;
            SensorsHasData = entity.SensorsHasData;
            HasTelegramNotifications = entity.HasTelegramNotifications;
            IsIgnoredSensors = entity.IsIgnoredSensors;
            IsBlockedSensors = entity.IsBlockedSensors;
            TreeUpdateInterval = entity.TreeUpdateInterval;
        }


        internal FilterEntity ToEntity() =>
            new()
            {
                HasOkStatus = HasOkStatus,
                HasWarningStatus = HasWarningStatus,
                HasErrorStatus = HasErrorStatus,
                HasUnknownStatus = HasUnknownStatus,
                SensorsHasData = SensorsHasData,
                HasTelegramNotifications = HasTelegramNotifications,
                IsIgnoredSensors = IsIgnoredSensors,
                IsBlockedSensors = IsBlockedSensors,
                TreeUpdateInterval = TreeUpdateInterval
            };
    }
}
