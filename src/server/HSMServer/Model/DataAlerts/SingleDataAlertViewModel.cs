using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.DataAlerts
{
    public sealed class SingleDataAlertViewModel<T, U> : DataAlertViewModel<T> where T : BaseValue<U>, new()
    {
        public SingleDataAlertViewModel(NodeViewModel node) : base(node) { }

        public SingleDataAlertViewModel(Policy<T, U> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }


        protected override ConditionViewModel CreateCondition(bool isMain) => new SingleConditionViewModel<T, U>(isMain);
    }
}