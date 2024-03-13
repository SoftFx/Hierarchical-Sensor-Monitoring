using HSMServer.Extensions;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.UserTreeShallowCopy
{
    public sealed class FolderShallowModel : BaseShallowModel<FolderModel>
    {
        public List<NodeShallowModel> Products { get; } = new(1 << 4);


        public IntegrationState GrafanaState { get; } = new();

        public AlertsState AlertsState { get; } = new();


        public override bool CurUserIsManager { get; }


        public override bool IsGrafanaEnabled => GrafanaState.IsAllEnabled;

        public override bool HasUnconfiguredAlerts => AlertsState.IsAnyEnabled;


        public bool AllDataIsEmpty => Products.All(n => n.Data.IsEmpty);

        public bool IsEmpty => Products.Count == 0;


        public FolderShallowModel(FolderModel data, User user) : base(data)
        {
            CurUserIsManager = user.IsFolderManager(data.Id);
        }


        internal void AddChild(NodeShallowModel node, User user)
        {
            node.Parent = this;

            ErrorsCount += node.ErrorsCount;

            GrafanaState.CalculateState(node.GrafanaState);
            AlertsState.CalculateState(node.AlertsState);

            if (node.VisibleSubtreeSensorsCount > 0 || user.IsEmptyProductVisible(node.Data))
                Products.Add(node);
        }

        public override string ToJSTree() =>
        $$"""
        {
            "title": "{{Data.Title}}",
            "icon": "fa-regular fa-folder",
            "time": "{{Data.UpdateTime.Ticks}}",
            "isManager": "{{CurUserIsManager}}",
            "isGrafanaEnabled": "{{IsGrafanaEnabled}}"
        }
        """;
    }
}
