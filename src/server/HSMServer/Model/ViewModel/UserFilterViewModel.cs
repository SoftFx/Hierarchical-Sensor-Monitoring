using HSMServer.Core.Model.UserFilter;

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
            HasOkStatus = filter.ByStatus.Ok.Value;
            HasWarningStatus = filter.ByStatus.Warning.Value;
            HasErrorStatus = filter.ByStatus.Error.Value;
            HasUnknownStatus = filter.ByStatus.Unknown.Value;
            IsEmptyHistory = filter.ByHistory.Empty.Value;
            HasTelegramNotifications = filter.ByNotifications.Enabled.Value;
            IsIgnoredSensors = filter.ByNotifications.Ignored.Value;
            IsBlockedSensors = filter.ByState.Blocked.Value;
            TreeUpdateInterval = filter.TreeUpdateInterval;
            TreeSortType = (int)filter.TreeSortType;
        }


        public TreeUserFilter ToFilter() =>
            new()
            {
                ByStatus = new GroupByStatus()
                {
                    Ok = new FilterProperty() { Value = HasOkStatus },
                    Warning = new FilterProperty() { Value = HasWarningStatus },
                    Error = new FilterProperty() { Value = HasErrorStatus },
                    Unknown = new FilterProperty() { Value = HasUnknownStatus },
                },
                ByHistory = new GroupByHistory()
                {
                    Empty = new FilterProperty() { Value = IsEmptyHistory },
                },
                ByNotifications = new GroupByNotifications()
                {
                    Enabled = new FilterProperty() { Value = HasTelegramNotifications },
                    Ignored = new FilterProperty() { Value = IsIgnoredSensors },
                },
                ByState = new GroupByState()
                {
                    Blocked = new FilterProperty() { Value = IsBlockedSensors },
                },
                TreeUpdateInterval = TreeUpdateInterval,
                TreeSortType = (TreeSortType)TreeSortType
            };
    }
}
