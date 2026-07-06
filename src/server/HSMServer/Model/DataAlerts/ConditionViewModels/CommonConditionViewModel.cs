using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class CommonConditionViewModel : ConditionViewModel
    {
        public static List<AlertProperty> SupportedProperties { get; } = new()
        {
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };

        protected override List<AlertProperty> Properties => SupportedProperties;


        public CommonConditionViewModel(bool isMain) : base(isMain) { }
    }
}
