using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;
using System.Numerics;

namespace HSMServer.Model.DataAlerts
{
    public abstract class NumberConditionViewModel : ConditionViewModel
    {
        protected override List<PolicyOperation> Operations { get; } = new()
        {
            PolicyOperation.LessThanOrEqual,
            PolicyOperation.LessThan,
            PolicyOperation.GreaterThan,
            PolicyOperation.GreaterThanOrEqual,
            PolicyOperation.NotEqual,
            PolicyOperation.Equal,
        };


        protected NumberConditionViewModel(bool isMain) : base(isMain) { }
    }


    public class SingleConditionViewModel<T, U> : NumberConditionViewModel where T : BaseValue<U>, new()
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Value,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
        };


        public SingleConditionViewModel(bool isMain) : base(isMain) { }
    }


    public sealed class BarConditionViewModel<T, U> : NumberConditionViewModel where T : BarBaseValue<U>, new() where U : INumber<U>
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Min,
            AlertProperty.Max,
            AlertProperty.Mean,
            AlertProperty.LastValue,
            AlertProperty.Count,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
        };


        public BarConditionViewModel(bool isMain) : base(isMain) { }
    }
}
