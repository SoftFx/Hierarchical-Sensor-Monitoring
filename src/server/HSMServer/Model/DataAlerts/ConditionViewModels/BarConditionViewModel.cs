using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class BarConditionViewModel : ConditionViewModel
    {
        protected override List<PolicyProperty> Properties { get; } = new()
        {
            PolicyProperty.Min,
            PolicyProperty.Max,
            PolicyProperty.Mean,
            PolicyProperty.LastValue,
            PolicyProperty.Count,
            PolicyProperty.Status,
            PolicyProperty.Comment,
            PolicyProperty.NewSensorData,
        };


        public BarConditionViewModel(bool isMain) : base(isMain) { }
    }
}
