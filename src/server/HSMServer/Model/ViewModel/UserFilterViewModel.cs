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


        public bool IsGroupNotificationsEnabled { get; set; }

        public bool IsAccountNotificationsEnabled { get; set; }

        public bool IsGroupNotificationsIgnored { get; set; }

        public bool IsAccountNotificationsIgnored { get; set; }


        public bool IsMutedSensorsState { get; set; }


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

            IsAccountNotificationsIgnored = filter.ByNotifications.AccountIgnored.Value;
            IsGroupNotificationsIgnored = filter.ByNotifications.GroupIgnored.Value;
            IsAccountNotificationsEnabled = filter.ByNotifications.AccountEnabled.Value;
            IsGroupNotificationsEnabled = filter.ByNotifications.GroupEnabled.Value;

            IsMutedSensorsState = filter.ByState.Muted.Value;

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

            filter.ByNotifications.AccountEnabled.Value = IsAccountNotificationsEnabled;
            filter.ByNotifications.GroupEnabled.Value = IsGroupNotificationsEnabled;
            filter.ByNotifications.AccountIgnored.Value = IsAccountNotificationsIgnored;
            filter.ByNotifications.GroupIgnored.Value = IsGroupNotificationsIgnored;

            filter.ByState.Muted.Value = IsMutedSensorsState;

            return filter;
        }
    }
}
