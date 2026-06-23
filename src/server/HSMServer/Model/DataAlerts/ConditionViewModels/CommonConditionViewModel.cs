using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class CommonConditionViewModel : ConditionViewModel
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
            AlertProperty.TimeToLive,
        };


        public CommonConditionViewModel(bool isMain) : base(isMain) { }
    }
}
