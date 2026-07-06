using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class NumericConditionViewModel : ConditionViewModel
    {
        public static List<AlertProperty> SupportedProperties { get; } = new()
        {
            AlertProperty.Value,
            AlertProperty.EmaValue,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };

        protected override List<AlertProperty> Properties => SupportedProperties;


        public NumericConditionViewModel(bool isMain) : base(isMain) { }
    }
}
