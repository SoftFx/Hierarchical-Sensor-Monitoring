using HSMDataCollector.Options;
using HSMDataCollector.SensorsMetainfo;

namespace HSMDataCollector.Converters
{
    internal static class PublicConverters
    {
        internal static SensorMetainfo ToInfo(this SensorOptions2 options)
        {
            var info = new SensorMetainfo()
            {
                Enables = new SensorMetainfoEnables()
                {
                    ForGrafana = options.EnableForGrafana
                },

                Description = options.Description,
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
