using HSMCommon.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public class BarDataAlertViewModel<T> : DataAlertViewModel<T> where T : BarBaseValue
    {
        public BarDataAlertViewModel() : base() { }

        public BarDataAlertViewModel(NodeViewModel node) : base(node) { }

        public BarDataAlertViewModel(Policy<T> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }

        protected override ConditionViewModel CreateCondition(bool isMain) => new BarConditionViewModel(isMain);
    }
}