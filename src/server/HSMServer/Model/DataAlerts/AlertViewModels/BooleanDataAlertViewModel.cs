using HSMCommon.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public sealed class BooleanDataAlertViewModel : DataAlertViewModel<BooleanValue>
    {
        public override SensorType Type => SensorType.Boolean;


        public BooleanDataAlertViewModel() : base() { }

        public BooleanDataAlertViewModel(NodeViewModel node) : base(node) { }

        public BooleanDataAlertViewModel(Policy<BooleanValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }
    }
}