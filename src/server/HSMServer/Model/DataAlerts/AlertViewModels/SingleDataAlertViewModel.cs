using HSMCommon.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;


namespace HSMServer.Model.DataAlerts
{
    public class SingleDataAlertViewModel<T> : DataAlertViewModel<T> where T : BaseValue
    {
        public SingleDataAlertViewModel() : base() { }

        public SingleDataAlertViewModel(NodeViewModel node) : base(node) { }

        public SingleDataAlertViewModel(Policy<T> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }


        protected override ConditionViewModel CreateCondition(bool isMain) => new SingleConditionViewModel(isMain);
    }
}