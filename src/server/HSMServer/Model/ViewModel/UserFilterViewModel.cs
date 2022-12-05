using HSMServer.Core.Model.UserFilters;

namespace HSMServer.Model.ViewModel
{
    public class UserFilterViewModel
    {
        public bool HasOkStatus { get; set; }
        public bool HasWarningStatus { get; set; }
        public bool HasErrorStatus { get; set; }
        public bool HasOffTimeStatus { get; set; }

        public bool IsEmptyHistory { get; set; }

        public bool HasTelegramNotifications { get; set; }
        public bool IsIgnoredSensors { get; set; }

        public bool IsBlockedSensors { get; set; }

        public int TreeUpdateInterval { get; set; }

        public int TreeSortType { get; set; }


        public UserFilterViewModel() { }

        public UserFilterViewModel(TreeUserFilter filter)
        {
            HasOkStatus = filter.ByStatus.Ok.Value;
            HasWarningStatus = filter.ByStatus.Warning.Value;
            HasErrorStatus = filter.ByStatus.Error.Value;
            HasOffTimeStatus = filter.ByStatus.OffTime.Value;

            IsEmptyHistory = filter.ByHistory.Empty.Value;

            HasTelegramNotifications = filter.ByNotifications.Enabled.Value;
            IsIgnoredSensors = filter.ByNotifications.Ignored.Value;

            IsBlockedSensors = filter.ByState.Blocked.Value;

            TreeUpdateInterval = filter.TreeUpdateInterval;
            TreeSortType = (int)filter.TreeSortType;
        }


        public TreeUserFilter ToFilter()
        {
            var filter = new TreeUserFilter()
            {
                TreeUpdateInterval = TreeUpdateInterval,
                TreeSortType = (TreeSortType)TreeSortType
            };

            filter.ByStatus.Ok.Value = HasOkStatus;
            filter.ByStatus.Warning.Value = HasWarningStatus;
            filter.ByStatus.Error.Value = HasErrorStatus;
            filter.ByStatus.OffTime.Value = HasOffTimeStatus;

            filter.ByHistory.Empty.Value = IsEmptyHistory;

            filter.ByNotifications.Enabled.Value = HasTelegramNotifications;
            filter.ByNotifications.Ignored.Value = IsIgnoredSensors;

            filter.ByState.Blocked.Value = IsBlockedSensors;

            return filter;
        }
    }
}
