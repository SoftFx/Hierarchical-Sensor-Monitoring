using HSMCommon.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public sealed class RateDataAlertViewModel : NumericDataAlertViewModel<RateValue>
    {
        public override SensorType Type => SensorType.Rate;

        public RateDataAlertViewModel() : base() { }

        public RateDataAlertViewModel(NodeViewModel node) : base(node) { }

        public RateDataAlertViewModel(Policy<RateValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }
    }
}
