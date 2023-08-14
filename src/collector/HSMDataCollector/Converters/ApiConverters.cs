using HSMDataCollector.SensorsMetainfo;
using HSMSensorDataObjects.SensorRequests;
using System.Linq;

namespace HSMDataCollector.Converters
{
    internal static class ApiConverters
    {
        internal static AddOrUpdateSensorRequest ToApi(this SensorMetainfo info) =>
            new AddOrUpdateSensorRequest()
            {
                Description = info.Description,
                SensorType = info.SensorType,
                Path = info.Path,

                SelectedUnit = info.Units.Selected,

                KeepHistory = info.Settings.KeepHistory?.Ticks,
                SelfDestroy = info.Settings.SelfDestroy?.Ticks,
                TTL = info.Settings.TTL?.Ticks,

                SaveOnlyUniqueValues = info.OnlyUniqValues,

                EnableGrafana = info.Enables.ForGrafana,
            };
    }
}
