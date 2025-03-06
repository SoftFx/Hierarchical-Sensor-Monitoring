using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public sealed class DoubleBarDataAlertViewModel : BarDataAlertViewModel<DoubleBarValue>
    {
        public override SensorType Type => SensorType.Boolean;


        public DoubleBarDataAlertViewModel() : base() { }

        public DoubleBarDataAlertViewModel(NodeViewModel node) : base(node) { }

        public DoubleBarDataAlertViewModel(Policy<DoubleBarValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }
    }
}