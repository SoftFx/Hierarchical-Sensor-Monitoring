using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.DataAlerts
{
    public sealed class DoubleDataAlertViewModel : NumericDataAlertViewModel<DoubleValue>
    {
        public override SensorType Type => SensorType.Boolean;

        public DoubleDataAlertViewModel() : base() { }

        public DoubleDataAlertViewModel(NodeViewModel node) : base(node) { }

        public DoubleDataAlertViewModel(Policy<DoubleValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }
    }
}
