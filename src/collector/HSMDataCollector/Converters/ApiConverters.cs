using HSMDataCollector.SensorsMetainfo;
using HSMSensorDataObjects.SensorRequests;
using System.Linq;

namespace HSMDataCollector.Converters
{
    internal static class ApiConverters
    {
        internal static SensorUpdateRequest ToApi(this SensorMetainfo info) =>
            new SensorUpdateRequest()
            {
                Description = info.Description,
                Path = info.Path,

                AvailableUnites = info.Units.AvailableUnits.Select(x => (int)x).ToList(),
                SelectedUnit = (int)info.Units.Selected,

                KeepHistory = info.Settings.KeepHistory?.Ticks,
                SelfDestroy = info.Settings.SelfDestroy?.Ticks,
                TTL = info.Settings.TTL?.Ticks,

                SaveOnlyUniqueValues = info.OnlyUniqValues,

                EnableGrafana = info.Enables.ForGrafana,
            };
    }
}
