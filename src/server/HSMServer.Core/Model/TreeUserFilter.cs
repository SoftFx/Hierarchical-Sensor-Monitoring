using System;

namespace HSMServer.Core.Model
{
    [Flags]
    public enum FilterGroups
    {
        ByStatus = 1,
        ByHistory = 2,
        ByNotifications = 4,
        ByState = 8,
    }

    public enum TreeSortType : int
    {
        ByName = 0,
        ByTime = 1
    }


    public sealed class TreeUserFilter
    {
        private const int DefaultInterval = 5;


        private bool HasFilterByStatus => HasOkStatus || HasWarningStatus || HasErrorStatus || HasUnknownStatus;

        private bool HasFilterByHistory => IsEmptyHistory;

        private bool HasFilterByNotifications => HasTelegramNotifications || IsIgnoredSensors;

        private bool HasFilterByState => IsBlockedSensors;


        public bool HasOkStatus { get; set; }

        public bool HasWarningStatus { get; set; }

        public bool HasErrorStatus { get; set; }

        public bool HasUnknownStatus { get; set; }


        public bool IsEmptyHistory { get; set; }


        public bool HasTelegramNotifications { get; set; }

        public bool IsIgnoredSensors { get; set; }


        public bool IsBlockedSensors { get; set; }


        public int TreeUpdateInterval { get; set; } = DefaultInterval;


        public TreeSortType TreeSortType { get; set; } = TreeSortType.ByName;


        public TreeUserFilter() { }

        internal TreeUserFilter(TreeUserFilter filter)
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
            TreeSortType = filter.TreeSortType;
        }


        public FilterGroups ToMask()
        {
            FilterGroups selectedFiltersMask = 0;

            if (HasFilterByStatus)
                selectedFiltersMask |= FilterGroups.ByStatus;
            if (HasFilterByHistory)
                selectedFiltersMask |= FilterGroups.ByHistory;
            if (HasFilterByNotifications)
                selectedFiltersMask |= FilterGroups.ByNotifications;
            if (HasFilterByState)
                selectedFiltersMask |= FilterGroups.ByState;

            return selectedFiltersMask;
        }
    }
}
