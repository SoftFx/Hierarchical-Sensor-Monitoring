using HSMServer.Core.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class SensorShallowModel : BaseShallowModel<SensorNodeViewModel>
    {
        public override bool IsAccountsEnable { get; }

        public override bool IsGroupsEnable { get; }
        
        internal SensorShallowModel(SensorNodeViewModel data, User user) : base(data, user)
        {
            IsAccountsEnable = user.Notifications.IsSensorEnabled(data.Id);
            IsGroupsEnable = data.RootProduct.Notifications.IsSensorEnabled(data.Id);
            IsIgnoredState = data.State == SensorState.Ignored;
        }
    }
}
