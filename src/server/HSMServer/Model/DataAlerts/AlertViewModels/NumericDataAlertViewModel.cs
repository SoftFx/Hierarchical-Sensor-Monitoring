using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;

namespace HSMServer.Model.DataAlerts
{
    public sealed class NumericDataAlertViewModel<T> : DataAlertViewModel<T> where T : BaseValue
    {
        public NumericDataAlertViewModel(NodeViewModel node) : base(node) { }

        public NumericDataAlertViewModel(Policy<T> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }


        protected override ConditionViewModel CreateCondition(bool isMain) => new NumericConditionViewModel(isMain);
    }
}
