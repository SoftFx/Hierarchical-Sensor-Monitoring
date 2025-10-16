using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.DataAlerts
{
    public sealed class IntegerDataAlertViewModel : NumericDataAlertViewModel<IntegerValue>
    {
        public override SensorType Type => SensorType.Integer;

        public IntegerDataAlertViewModel() : base() { }

        public IntegerDataAlertViewModel(NodeViewModel node) : base(node) { }

        public IntegerDataAlertViewModel(Policy<IntegerValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }
    }
}
