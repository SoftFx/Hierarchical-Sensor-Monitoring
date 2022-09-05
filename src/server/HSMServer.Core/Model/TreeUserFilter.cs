﻿namespace HSMServer.Core.Model
{
    public sealed class TreeUserFilter
    {
        private const int DefaultInterval = 5;

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
    }

    public enum TreeSortType : int
    {
        ByName = 0,
        ByTime = 1
    }
}
