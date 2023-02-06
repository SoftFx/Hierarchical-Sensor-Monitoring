using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.UserFilters
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

        private UserFilterGroupBase[] Groups =>
            new UserFilterGroupBase[] { ByStatus, ByHistory, ByNotifications, ByState };


        public GroupByStatus ByStatus { get; init; } = new();

        public GroupByHistory ByHistory { get; init; } = new();

        public GroupByNotifications ByNotifications { get; init; } = new();

        public GroupByState ByState { get; init; } = new();


        public int TreeUpdateInterval { get; init; } = DefaultInterval;

        public TreeSortType TreeSortType { get; init; } = TreeSortType.ByName;


        [JsonIgnore]
        public int EnabledFiltersCount => Groups.Sum(g => g.EnableFiltersCount);

        [JsonIgnore]
        public string EnabledFiltersMessage => GetEnabledFiltersMessage();

        public TreeUserFilter() { }


        public FilterGroupType ToMask()
        {
            FilterGroupType selectedFiltersMask = 0;

            foreach (var group in Groups)
                if (group.HasAnyEnabledFilters)
                    selectedFiltersMask |= group.Type;

            return selectedFiltersMask;
        }

        public bool IsSensorVisible(FilteredSensor sensor)
        {
            var mask = ToMask();
            var isSensorVisible = true;

            foreach (var group in Groups)
                if (group.NeedToCheckSensor(mask))
                    isSensorVisible &= group.IsSensorSuitable(sensor);

            return isSensorVisible;
        }

        private string GetEnabledFiltersMessage()
        {
            var filters = new List<string>(1 << 4);
            foreach (var group in Groups)
            {
                if (!group.HasAnyEnabledFilters) continue;

                var specificFilters = new List<string>(1 << 2);

                var tittle = $"{group.Type}: ";
                specificFilters.AddRange(group.Properties.Where(property => property.Value).Select(property => property.Name));
                
                filters.Add($"{tittle} {string.Join(", ",specificFilters)}");
            }
                
            return $"Enabled filters: \n{string.Join('\n', filters)}";
        }
    }
}