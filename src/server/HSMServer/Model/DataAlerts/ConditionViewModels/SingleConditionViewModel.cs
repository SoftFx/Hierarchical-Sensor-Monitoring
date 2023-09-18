using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class SingleConditionViewModel : ConditionViewModel
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
}
