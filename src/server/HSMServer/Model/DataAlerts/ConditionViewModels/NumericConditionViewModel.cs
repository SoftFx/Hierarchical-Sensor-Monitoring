using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class NumericConditionViewModel : ConditionViewModel
    {
        public static IReadOnlyList<AlertProperty> SupportedProperties { get; } = new[]
        {
            AlertProperty.Value,
            AlertProperty.EmaValue,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };

        protected override IReadOnlyList<AlertProperty> Properties => SupportedProperties;


        public NumericConditionViewModel(bool isMain) : base(isMain) { }
    }
}
