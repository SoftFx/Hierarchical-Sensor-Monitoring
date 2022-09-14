using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Model.UserFilter
{
    [Flags]
    public enum FilterGroupType
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

        private readonly List<UserFilterGroupBase> _groups = new();


        public GroupByStatus ByStatus { get; init; } = new();

        public GroupByHistory ByHistory { get; init; } = new();

        public GroupByNotifications ByNotifications { get; init; } = new();

        public GroupByState ByState { get; init; } = new();


        public int TreeUpdateInterval { get; init; } = DefaultInterval;

        public TreeSortType TreeSortType { get; init; } = TreeSortType.ByName;


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


        public FilterGroupType ToMask()
        {
            FilterGroupType selectedFiltersMask = 0;

            foreach (var group in _groups)
                if (group.HasAnyEnabledFilters)
                    selectedFiltersMask |= group.Type;

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
