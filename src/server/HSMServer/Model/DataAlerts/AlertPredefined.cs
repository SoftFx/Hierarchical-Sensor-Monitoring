using HSMCommon.Extensions;
using HSMServer.Extensions;
using HSMServer.Model.TreeViewModel;
using System.Collections.Generic;

namespace HSMServer.Model.DataAlerts
{
    public static class AlertPredefined
    {
        public static Dictionary<SensorStatus, string> Statuses { get; } = new()
        {
            { SensorStatus.Error, $"{SensorStatus.Error.ToSelectIcon()} {SensorStatus.Error.GetDisplayName()}" },
        };
    }
}
