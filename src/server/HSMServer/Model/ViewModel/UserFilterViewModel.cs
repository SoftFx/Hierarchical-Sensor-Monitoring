using HSMServer.Core.Model;

namespace HSMServer.Model.ViewModel
{
    public class UserFilterViewModel
    {
        public bool HasOkStatus { get; set; }
        public bool HasWarningStatus { get; set; }
        public bool HasErrorStatus { get; set; }
        public bool HasUnknownStatus { get; set; }

        public bool IsEmptyHistory { get; set; }

        public bool HasTelegramNotifications { get; set; }
        public bool IsIgnoredSensors { get; set; }

        public bool IsBlockedSensors { get; set; }

        public int TreeUpdateInterval { get; set; }

        public int TreeSortType { get; set; }


        public UserFilterViewModel() { }

        public UserFilterViewModel(TreeUserFilter filter)
        {
            HasOkStatus = filter.HasOkStatus;
            HasWarningStatus = filter.HasWarningStatus;
            HasErrorStatus = filter.HasErrorStatus;
            HasUnknownStatus = filter.HasUnknownStatus;
            IsEmptyHistory = filter.IsEmptyHistory;
            HasTelegramNotifications = filter.HasTelegramNotifications;
            IsIgnoredSensors = filter.IsIgnoredSensors;
            IsBlockedSensors = filter.IsBlockedSensors;
            TreeUpdateInterval = filter.TreeUpdateInterval;
            TreeSortType = (int)filter.TreeSortType;
        }


        public TreeUserFilter ToFilter() =>
            new ()
            {
                HasOkStatus = HasOkStatus,
                HasWarningStatus = HasWarningStatus,
                HasErrorStatus = HasErrorStatus,
                HasUnknownStatus = HasUnknownStatus,
                IsEmptyHistory = IsEmptyHistory,
                HasTelegramNotifications = HasTelegramNotifications,
                IsIgnoredSensors = IsIgnoredSensors,
                IsBlockedSensors = IsBlockedSensors,
                TreeUpdateInterval = TreeUpdateInterval,
                TreeSortType = (TreeSortType)TreeSortType
            };
    }
}
