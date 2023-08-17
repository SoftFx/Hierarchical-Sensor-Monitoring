using HSMDataCollector.Alerts;
using HSMDataCollector.Options;
using HSMDataCollector.SensorsMetainfo;
using System.Linq;

namespace HSMDataCollector.Converters
{
    internal static class PublicConverters
    {
        internal static SensorMetainfo ToInfo(this InstantSensorOptions options)
        {
            var info = options.ToBaseInfo();

            info.Alerts = options.Alerts?.Select(u => (AlertBuildRequest)u).ToList();

            return info;
        }


        internal static SensorMetainfo ToInfo(this BarSensorOptions2 options)
        {
            var info = options.ToBaseInfo();

            info.Alerts = options.Alerts?.Select(u => (AlertBuildRequest)u).ToList();

            return info;
        }


        private static SensorMetainfo ToBaseInfo(this SensorOptions2 options)
        {
            var info = new SensorMetainfo()
            {
                Enables = new SensorMetainfoEnables()
                {
                    ForGrafana = options.EnableForGrafana
                },

                Description = options.Description,
                SensorType = options.Type,
                Path = options.Path,

                OnlyUniqValues = options.OnlyUniqValues,
            };

            if (options.HasSettings)
                info.Settings = new SensorMetainfoSettings()
                {
                    KeepHistory = options.KeepHistory,
                    SelfDestroy = options.SelfDestroy,
                    TTL = options.TTL,
                };

            if (options.SensorUnit != null)
                info.Units = new SensorMetainfoUnits(options.SensorUnit.Value);

            return info;
        }
    }
}
