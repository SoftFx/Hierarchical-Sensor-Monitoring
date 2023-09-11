using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Model.TreeViewModel;
using System.Numerics;

namespace HSMServer.Model.DataAlerts
{
    public sealed class BarDataAlertViewModel<T, U> : DataAlertViewModel<T> where T : BarBaseValue<U>, new() where U : INumber<U>
    {
        public BarDataAlertViewModel(NodeViewModel node) : base(node) { }

        public BarDataAlertViewModel(Policy<T> policy, SensorNodeViewModel sensor) : base(policy, sensor) { }


        protected override ConditionViewModel CreateCondition(bool isMain) => new BarConditionViewModel<T, U>(isMain);
    }
}