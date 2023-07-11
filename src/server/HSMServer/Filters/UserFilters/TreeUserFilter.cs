using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace HSMServer.UserFilters
{
    [Flags]
    public enum FilterGroupType
    {
        ByStatus = 1,
        ByVisibility = 2,
        Notifications = 4,
        ByState = 8,
        Integrations = 16,
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
            new UserFilterGroupBase[] { ByStatus, ByVisibility, ByNotifications, ByState, ByIntegrations};


        public GroupByStatus ByStatus { get; init; } = new();

        public GroupByVisibility ByVisibility { get; init; } = new();

        public GroupByNotifications ByNotifications { get; init; } = new();

        public GroupByState ByState { get; init; } = new();
        
        public GroupByIntegrations ByIntegrations { get; init; } = new();



        public int TreeUpdateInterval { get; init; } = DefaultInterval;

        public TreeSortType TreeSortType { get; init; } = TreeSortType.ByName;


        [JsonIgnore]
        public int EnabledFiltersCount => Groups.Sum(g => g.EnableFiltersCount);

        [JsonIgnore]
        public string EnabledFiltersMessage => GetEnabledFiltersMessage();

        public TreeUserFilter() { }


        public TreeUserFilter RestoreFilterNames()
        {
            var tempUserFilter = new TreeUserFilter();

            for (int i = 0; i < Groups.Length; i++)
            {
                for (int j = 0; j < Groups[i].Properties.Length; j++)
                {
                    Groups[i].Properties[j].Name = tempUserFilter.Groups[i].Properties[j].Name;
                }
            }

            return this;
        }
        
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
            var filters = new StringBuilder(1 << 4);
            var specificFilters = new List<string>(1 << 2);
            foreach (var group in Groups)
            {
                if (!group.HasAnyEnabledFilters) 
                    continue;
                
                specificFilters.AddRange(group.Properties.Where(property => property.Value).Select(property => property.Name));
                
                filters.AppendLine($"{group.Type}: {string.Join(", ",specificFilters)}");
                specificFilters.Clear();
            }
                
            return $"{filters}";
        }
    }
}