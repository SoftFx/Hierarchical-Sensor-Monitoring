using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class FileConditionViewModel : ConditionViewModel
    {
        public static List<AlertProperty> SupportedProperties { get; } = new()
        {
            AlertProperty.OriginalSize,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };

        protected override List<AlertProperty> Properties => SupportedProperties;


        public FileConditionViewModel(bool isMain) : base(isMain) { }
    }
}
