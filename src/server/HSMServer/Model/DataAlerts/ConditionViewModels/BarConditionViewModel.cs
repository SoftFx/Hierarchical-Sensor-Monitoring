using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class BarConditionViewModel : ConditionViewModel
    {
        public static IReadOnlyList<AlertProperty> SupportedProperties { get; } = new[]
        {
            AlertProperty.FirstValue,
            AlertProperty.LastValue,
            AlertProperty.Min,
            AlertProperty.Mean,
            AlertProperty.Max,
            AlertProperty.Count,
            AlertProperty.EmaMin,
            AlertProperty.EmaMean,
            AlertProperty.EmaMax,
            AlertProperty.EmaCount,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };

        protected override IReadOnlyList<AlertProperty> Properties => SupportedProperties;


        public BarConditionViewModel(bool isMain) : base(isMain) { }
    }
}
