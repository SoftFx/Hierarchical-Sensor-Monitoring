using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class VersionConditionViewModel : ConditionViewModel
    {
        public static List<AlertProperty> SupportedProperties { get; } = new()
        {
            AlertProperty.Value,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };

        protected override List<AlertProperty> Properties => SupportedProperties;


        public VersionConditionViewModel(bool isMain) : base(isMain) { }
    }
}
