using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class BarConditionViewModel : ConditionViewModel
    {
        protected override List<AlertProperty> Properties { get; } = new()
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
        };


        public BarConditionViewModel(bool isMain) : base(isMain) { }
    }
}
