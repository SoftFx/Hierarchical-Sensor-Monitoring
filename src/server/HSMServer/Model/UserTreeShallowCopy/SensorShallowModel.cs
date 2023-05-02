using HSMServer.Core;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using System;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class SensorShallowModel : BaseNodeShallowModel<SensorNodeViewModel>
    {
        public override bool IsGrafanaEnabled { get; }

        public override bool IsAccountsEnable { get; }

        public override bool IsAccountsIgnore { get; }

        public override bool IsGroupsEnable { get; }

        public override bool IsGroupsIgnore { get; }


        internal SensorShallowModel(SensorNodeViewModel data, User user) : base(data, user)
        {
            IsGrafanaEnabled = data.Integration.HasGrafana();
            IsAccountsEnable = user.Notifications.IsSensorEnabled(data.Id);
            IsGroupsEnable = data.RootProduct.Notifications.IsSensorEnabled(data.Id);

            _mutedValue = data.State == SensorState.Muted;

            IsGroupsIgnore = data.RootProduct.Notifications.IgnoredSensors.TryGetValue(data.Id, out var accountTime) && accountTime != DateTime.MaxValue;
            IsAccountsIgnore = user.Notifications.IgnoredSensors.TryGetValue(data.Id, out var groupTime) && groupTime != DateTime.MaxValue;
        }
    }
}