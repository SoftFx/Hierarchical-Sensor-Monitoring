using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class FolderShallowModel : BaseShallowModel<FolderModel>
    {
        public List<NodeShallowModel> Nodes { get; } = new(1 << 4);


        public UserNotificationsState GroupState { get; } = new();

        public UserNotificationsState AccountState { get; } = new();


        public override bool CurUserIsManager { get; }


        public override bool IsAccountsEnable => AccountState.IsAllEnabled;

        public override bool IsGroupsEnable => GroupState.IsAllEnabled;


        public bool IsEmpty => Nodes.All(n => n.Data.IsEmpty);


        public FolderShallowModel(FolderModel data, User user) : base(data)
        {
            CurUserIsManager = user.IsFolderManager(user.Id);
        }

        internal void AddChild(NodeShallowModel node, User user)
        {
            if (!node.IsMutedState)
            {
                AccountState.CalculateState(node.AccountState);
                GroupState.CalculateState(node.GroupState);
            }

            if (node.VisibleSensorsCount > 0 || user.IsEmptyProductVisible(node.Data))
                Nodes.Add(node);
        }

        public override string ToJSTree() =>
        $$"""
        {
            "title": "{{Data.Title}}",
            "icon": "fa-regular fa-folder",
            "time": "{{Data.UpdateTime.Ticks}}",
            "isManager": "{{CurUserIsManager}}",
            "isAccountsEnable": "{{IsAccountsEnable}}",
            "isGroupsEnable": "{{IsGroupsEnable}}"
        }
        """;
    }
}
