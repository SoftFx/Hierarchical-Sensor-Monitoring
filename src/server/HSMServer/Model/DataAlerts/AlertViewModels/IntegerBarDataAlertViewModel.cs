using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public sealed class IntegerBarDataAlertViewModel : BarDataAlertViewModel<IntegerBarValue>
    {
        public override SensorType Type => SensorType.IntegerBar;

        public IntegerBarDataAlertViewModel() : base() { }

        public IntegerBarDataAlertViewModel(NodeViewModel node) : base(node) { }

        public IntegerBarDataAlertViewModel(Policy<IntegerBarValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }
    }
}