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
            { SensorStatus.Warning, $"{SensorStatus.Warning.ToSelectIcon()} {SensorStatus.Warning.GetDisplayName()}" },
            { SensorStatus.Error, $"{SensorStatus.Error.ToSelectIcon()} {SensorStatus.Error.GetDisplayName()}" },
        };

        public static List<string> BorderIcons { get; } = new() { "⬆️", "⏫", "🔼", "↕️", "🔽", "⏬", "⬇️" };
    }
}
