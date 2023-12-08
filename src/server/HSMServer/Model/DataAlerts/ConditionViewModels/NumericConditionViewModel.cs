using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class NumericConditionViewModel : ConditionViewModel
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Value,
            AlertProperty.EmaValue,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
        };


        public NumericConditionViewModel(bool isMain) : base(isMain) { }
    }
}
