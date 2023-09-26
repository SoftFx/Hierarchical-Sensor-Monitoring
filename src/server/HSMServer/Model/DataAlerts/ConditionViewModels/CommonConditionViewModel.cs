using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class CommonConditionViewModel : ConditionViewModel
    {
        protected override List<PolicyProperty> Properties { get; } = new()
        {
            PolicyProperty.Status,
            PolicyProperty.Comment,
            PolicyProperty.NewSensorData,
        };


        public CommonConditionViewModel(bool isMain) : base(isMain) { }
    }
}
