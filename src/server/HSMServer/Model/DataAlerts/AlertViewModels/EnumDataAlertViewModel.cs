using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.DataAlerts
{
    public sealed class EnumDataAlertViewModel : NumericDataAlertViewModel<EnumValue>
    {
        public override SensorType Type => SensorType.Enum;

        public EnumDataAlertViewModel() : base() { }

        public EnumDataAlertViewModel(NodeViewModel node) : base(node) { }

        public EnumDataAlertViewModel(Policy<EnumValue> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }
    }
}
