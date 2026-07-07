using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class VersionConditionViewModel : ConditionViewModel
    {
        public static IReadOnlyList<AlertProperty> SupportedProperties { get; } = new[]
        {
            AlertProperty.Value,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };

        protected override IReadOnlyList<AlertProperty> Properties => SupportedProperties;


        public VersionConditionViewModel(bool isMain) : base(isMain) { }
    }
}
