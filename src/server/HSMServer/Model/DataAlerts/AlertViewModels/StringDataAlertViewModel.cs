using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public sealed class StringDataAlertViewModel : DataAlertViewModel<StringValue>
    {
        public override SensorType Type => SensorType.Boolean;

        public StringDataAlertViewModel() : base() { }

        public StringDataAlertViewModel(NodeViewModel node) : base(node) { }

        public StringDataAlertViewModel(Policy<StringValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }

        protected override ConditionViewModel CreateCondition(bool isMain) => new StringConditionViewModel(isMain);
    }
}
