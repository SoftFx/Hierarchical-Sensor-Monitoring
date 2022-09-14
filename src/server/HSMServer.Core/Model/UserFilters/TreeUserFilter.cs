using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.UserFilter
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

        private readonly List<UserFilterGroup> _groups = new();


        public GroupByStatus ByStatus { get; set; } = new();

        public GroupByHistory ByHistory { get; set; } = new();

        public GroupByNotifications ByNotifications { get; set; } = new();

        public GroupByState ByState { get; set; } = new();

        public int TreeUpdateInterval { get; set; } = DefaultInterval;

        public TreeSortType TreeSortType { get; set; } = TreeSortType.ByName;

        public int EnabledFiltersCount => _groups.Sum(g => g.EnableFiltersCount);


        public TreeUserFilter() { }

        internal TreeUserFilter(TreeUserFilter filter)
        {
            ByStatus = new(filter.ByStatus);
            ByHistory = new(filter.ByHistory);
            ByNotifications = new(filter.ByNotifications);
            ByState = new(filter.ByState);

            TreeUpdateInterval = filter.TreeUpdateInterval;
            TreeSortType = filter.TreeSortType;
        }


        public FilterGroups ToMask()
        {
            FilterGroups selectedFiltersMask = 0;

            if (ByStatus.HasAnyEnabledFilters)
                selectedFiltersMask |= FilterGroups.ByStatus;
            if (ByHistory.HasAnyEnabledFilters)
                selectedFiltersMask |= FilterGroups.ByHistory;
            if (ByNotifications.HasAnyEnabledFilters)
                selectedFiltersMask |= FilterGroups.ByNotifications;
            if (ByState.HasAnyEnabledFilters)
                selectedFiltersMask |= FilterGroups.ByState;

            return selectedFiltersMask;
        }

        internal void RegisterGroups()
        {
            _groups.Add(ByStatus);
            _groups.Add(ByHistory);
            _groups.Add(ByNotifications);
            _groups.Add(ByState);

            _groups.ForEach(g => g.RegisterProperties());
        }
    }
}
