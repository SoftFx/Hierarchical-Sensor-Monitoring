using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class SingleConditionViewModel : ConditionViewModel
    {
        protected override List<PolicyProperty> Properties { get; } = new()
        {
            PolicyProperty.Value,
            PolicyProperty.Status,
            PolicyProperty.Comment,
            PolicyProperty.NewSensorData,
        };


        public SingleConditionViewModel(bool isMain) : base(isMain) { }
    }
}
