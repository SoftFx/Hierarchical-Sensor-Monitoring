using HSMCommon.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public sealed class TimeSpanDataAlertViewModel : SingleDataAlertViewModel<TimeSpanValue>
    {
        public override SensorType Type => SensorType.TimeSpan;

        public TimeSpanDataAlertViewModel() : base() { }

        public TimeSpanDataAlertViewModel(NodeViewModel node) : base(node) { }

        public TimeSpanDataAlertViewModel(Policy<TimeSpanValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }
    }
}