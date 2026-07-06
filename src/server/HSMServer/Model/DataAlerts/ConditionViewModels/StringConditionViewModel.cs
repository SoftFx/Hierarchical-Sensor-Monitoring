using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class StringConditionViewModel : ConditionViewModel
    {
        public static List<AlertProperty> SupportedProperties { get; } = new()
        {
            AlertProperty.Value,
            AlertProperty.Length,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };

        protected override List<AlertProperty> Properties => SupportedProperties;


        public StringConditionViewModel(bool isMain) : base(isMain) { }
    }
}
