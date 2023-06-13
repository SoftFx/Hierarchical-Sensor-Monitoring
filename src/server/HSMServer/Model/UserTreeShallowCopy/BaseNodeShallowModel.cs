using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public abstract class BaseNodeShallowModel<T> : BaseShallowModel<T> where T : NodeViewModel
    {
        protected bool? _mutedValue;


        public override bool CurUserIsManager { get; }

        public BaseShallowModel Parent { get; internal set; }

        public bool IsMutedState => _mutedValue ?? false;


        protected BaseNodeShallowModel(T data, User user) : base(data)
        {
            CurUserIsManager = user.IsManager(data.RootProduct.Id);
        }


        public override string ToJSTree() =>
        $$"""
        {
            "title": "{{Data.Title}}",
            "icon": "{{(Data.HasData ? Data.Status.ToIcon() : NodeExtensions.GetEmptySensorIcon())}}",
            "time": "{{Data.UpdateTime.Ticks}}",
            "isManager": "{{CurUserIsManager}}",
            "isGrafanaEnabled": "{{IsGrafanaEnabled}}",
            "isAccountsEnable": "{{IsAccountsEnable}}",
            "groups": {{GroupsJsonDict}},
            "isMutedState": "{{_mutedValue}}"
        }
        """;

        public string ToDisabledJSTree() => 
        $$"""
        {
            "title": "{{Data.Title}}",
            "icon": "disabled",
            "time": "disabled",
            "isManager": "disabled",
            "isGrafanaEnabled": "disabled",
            "isAccountsEnable": "disabled",
            "groups": "disabled",
            "isMutedState": "disabled",
            "disabled": {{true.ToString().ToLower()}}
        }
        """;
    }
}