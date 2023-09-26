using HSMServer.Core.Model.Policies;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class FileConditionViewModel : ConditionViewModel
    {
        protected override List<PolicyProperty> Properties { get; } = new()
        {
            PolicyProperty.OriginalSize,
            PolicyProperty.Status,
            PolicyProperty.Comment,
            PolicyProperty.NewSensorData,
        };


        public FileConditionViewModel(bool isMain) : base(isMain) { }
    }
}
