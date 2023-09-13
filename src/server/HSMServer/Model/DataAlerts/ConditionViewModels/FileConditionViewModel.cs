using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public sealed class FileConditionViewModel : NumberConditionViewModel
    {
        protected override List<AlertProperty> Properties { get; } = new()
        {
            AlertProperty.OriginalSize,
            AlertProperty.Status,
            AlertProperty.Comment,
            AlertProperty.NewSensorData,
        };


        public FileConditionViewModel(bool isMain) : base(isMain) { }
    }
}
