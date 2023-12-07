using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class BarConditionViewModel : ConditionViewModel
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Min,
            AlertProperty.Max,
            AlertProperty.Mean,
            AlertProperty.FirstValue,
            AlertProperty.LastValue,
            AlertProperty.Count,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
        };


        public BarConditionViewModel(bool isMain) : base(isMain) { }
    }
}
