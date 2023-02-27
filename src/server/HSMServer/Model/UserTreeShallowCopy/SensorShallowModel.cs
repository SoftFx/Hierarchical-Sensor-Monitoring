using System;
using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class SensorShallowModel : BaseShallowModel<SensorNodeViewModel>
    {
        public override bool IsAccountsEnable { get; }

        public override bool IsGroupsEnable { get; }
        

        public bool IsAccountIgnoreIconShow { get; }
        
        public bool IsGroupIgnoreIconShow { get; }
        

        internal SensorShallowModel(SensorNodeViewModel data, User user) : base(data, user)
        {
            IsAccountsEnable = user.Notifications.IsSensorEnabled(data.Id);
            IsGroupsEnable = data.RootProduct.Notifications.IsSensorEnabled(data.Id);
            IsIgnoredState = data.State == SensorState.Ignored;

            IsGroupIgnoreIconShow = data.RootProduct.Notifications.IgnoredSensors.TryGetValue(data.Id, out var accountTime) && accountTime != DateTime.MaxValue;
            IsAccountIgnoreIconShow = user.Notifications.IgnoredSensors.TryGetValue(data.Id, out var groupTime) && groupTime != DateTime.MaxValue;
        }
    }
}
