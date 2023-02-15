using HSMServer.Core.Model;
using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class SensorShallowModel : BaseShallowModel<SensorNodeViewModel>
    {
        private readonly bool _curUserIsManager;
        
        public override bool IsAccountsEnable { get; }

        public override bool IsGroupsEnable { get; }
        
        public bool IsSensorIgnored { get; }

        internal SensorShallowModel(SensorNodeViewModel data, User user) : base(data, user)
        {
            IsAccountsEnable = user.Notifications.IsSensorEnabled(data.Id);
            IsGroupsEnable = data.GroupNotifications.IsSensorEnabled(data.Id);
            
            _curUserIsManager = user.IsManager(data.Parent?.Id ?? data.Id);
            
            IsSensorIgnored = data.State == SensorState.Ignored;
        }
        
        public string ToJSTree() =>
            $$"""
        {
            "title": "{{Data.Title}}",
            "icon": "{{Data.Status.ToIcon()}}",
            "time": "{{Data.UpdateTime.ToDefaultFormat()}}",
            "isManager": "{{_curUserIsManager}}",
            "isAccountsEnable": "{{IsAccountsEnable}}",
            "isGroupsEnable": "{{IsGroupsEnable}}",
            "isSensorIgnored": "{{IsSensorIgnored}}"
        }
        """;
    }
}
