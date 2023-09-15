using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.DataAlerts
{
    public sealed class SingleDataAlertViewModel<T> : DataAlertViewModel<T> where T : BaseValue
    {
        public SingleDataAlertViewModel(NodeViewModel node) : base(node) { }

        public SingleDataAlertViewModel(Policy<T> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }


        protected override ConditionViewModel CreateCondition(bool isMain) => new SingleConditionViewModel<T>(isMain);
    }
}