using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public class VersionConditionViewModel : ConditionViewModel
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Value,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };


        public VersionConditionViewModel(bool isMain) : base(isMain) { }
    }
}
