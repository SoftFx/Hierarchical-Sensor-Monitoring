using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class FileConditionViewModel : ConditionViewModel
    {
        public static IReadOnlyList<AlertProperty> SupportedProperties { get; } = new[]
        {
            AlertProperty.OriginalSize,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };

        protected override IReadOnlyList<AlertProperty> Properties => SupportedProperties;


        public FileConditionViewModel(bool isMain) : base(isMain) { }
    }
}
