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
            { SensorStatus.Ok, "not modify" },
            { SensorStatus.Warning, $"set {SensorStatus.Warning.ToSelectIcon()} {SensorStatus.Warning.GetDisplayName()}" },
            { SensorStatus.Error, $"set {SensorStatus.Error.ToSelectIcon()} {SensorStatus.Error.GetDisplayName()}" },
        };
    }
}
