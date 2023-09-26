using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class StringConditionViewModel : ConditionViewModel
    {
        protected override List<PolicyProperty> Properties { get; } = new()
        {
            PolicyProperty.Value,
            PolicyProperty.Length,
            PolicyProperty.Status,
            PolicyProperty.Comment,
            PolicyProperty.NewSensorData,
        };


        public StringConditionViewModel(bool isMain) : base(isMain) { }
    }
}
