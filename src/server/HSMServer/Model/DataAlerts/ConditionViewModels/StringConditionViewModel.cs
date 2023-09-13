using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class StringConditionViewModel : NumberConditionViewModel
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.Value,
            AlertProperty.Length,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
        };


        public StringConditionViewModel(bool isMain) : base(isMain) { }
    }
}
