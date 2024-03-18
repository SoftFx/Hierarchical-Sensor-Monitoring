﻿using HSMServer.UserFilters;

namespace HSMServer.Model.ViewModel
{
    public class UserFilterViewModel
    {
        public bool HasOkStatus { get; set; }

        public bool HasErrorStatus { get; set; }

        public bool HasOffTimeStatus { get; set; }


        public bool IsEmptyHistory { get; set; }


        public bool IsGrafanaEnabled { get; set; }


        public bool HasUnconfiguredAlerts { get; set; }


        public bool IsSensorsCountVisible { get; set; }

        public bool IsErrorsCountVisible { get; set; }

        public bool AreIconsVisible { get; set; }


        public bool IsMutedSensorsState { get; set; }


        public int TreeUpdateInterval { get; set; }

        public int TreeSortType { get; set; }


        public UserFilterViewModel() { }

        public UserFilterViewModel(TreeUserFilter filter)
        {
            HasOkStatus = filter.ByStatus.Ok.Value;
            HasErrorStatus = filter.ByStatus.Error.Value;
            HasOffTimeStatus = filter.ByStatus.OffTime.Value;

            IsEmptyHistory = filter.ByVisibility.Empty.Value;
            IsSensorsCountVisible = filter.ByVisibility.SensorsCount.Value;
            IsErrorsCountVisible = filter.ByVisibility.ErrorsCount.Value;
            AreIconsVisible = filter.ByVisibility.Icons.Value;

            IsGrafanaEnabled = filter.ByIntegrations.GrafanaEnabled.Value;

            HasUnconfiguredAlerts = filter.ByAlerts.HasUnconfiguredAlerts.Value;

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
            filter.ByStatus.Error.Value = HasErrorStatus;
            filter.ByStatus.OffTime.Value = HasOffTimeStatus;

            filter.ByVisibility.Empty.Value = IsEmptyHistory;
            filter.ByVisibility.SensorsCount.Value = IsSensorsCountVisible;
            filter.ByVisibility.ErrorsCount.Value = IsErrorsCountVisible;
            filter.ByVisibility.Icons.Value = AreIconsVisible;

            filter.ByIntegrations.GrafanaEnabled.Value = IsGrafanaEnabled;

            filter.ByAlerts.HasUnconfiguredAlerts.Value = HasUnconfiguredAlerts;

            filter.ByState.Muted.Value = IsMutedSensorsState;

            return filter;
        }
    }
}
