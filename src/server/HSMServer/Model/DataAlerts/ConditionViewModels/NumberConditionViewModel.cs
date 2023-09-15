using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

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


    public class SingleConditionViewModel<T> : NumberConditionViewModel where T : BaseValue
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


    public sealed class BarConditionViewModel<T> : NumberConditionViewModel where T : BarBaseValue
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
