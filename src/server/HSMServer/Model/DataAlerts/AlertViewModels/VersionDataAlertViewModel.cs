using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public sealed class VersionDataAlertViewModel : SingleDataAlertViewModel<VersionValue>
    {
        public override SensorType Type => SensorType.Version;


        public VersionDataAlertViewModel() : base() { }

        public VersionDataAlertViewModel(NodeViewModel node) : base(node) { }

        public VersionDataAlertViewModel(Policy<VersionValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }

        protected override ConditionViewModel CreateCondition(bool isMain) => new VersionConditionViewModel(isMain);

    }
}