using HSMCommon.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public sealed class DoubleDataAlertViewModel : NumericDataAlertViewModel<DoubleValue>
    {
        public override SensorType Type => SensorType.Double;

        public DoubleDataAlertViewModel() : base() { }

        public DoubleDataAlertViewModel(NodeViewModel node) : base(node) { }

        public DoubleDataAlertViewModel(Policy<DoubleValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }
    }
}
