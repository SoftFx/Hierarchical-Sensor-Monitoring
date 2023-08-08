using HSMDataCollector.Options;
using HSMDataCollector.SensorsMetainfo;

namespace HSMDataCollector.Prototypes
{
    internal abstract class BarBaseMonitoringPrototype<MetainfoType, OptionsType> : Prototype<MetainfoType, OptionsType>
        where MetainfoType : MonitoringSensorMetainfo, new()
        where OptionsType : BarSensorOptions
    {
        protected const string SystemPath = ".Default";


        protected abstract string SensorName { get; }

        protected virtual string Category { get; }


        protected BarBaseMonitoringPrototype()
        {
            Path = BuildPath(SystemPath, Category, SensorName);
        }


        protected override MetainfoType Apply(MetainfoType info, OptionsType options)
        {
            info.CollectBarPeriod = options.CollectBarPeriod;
            info.BarPeriod = options.BarPeriod;
            info.Precision = options.Precision;

            return info;
        }
    }


    internal abstract class BarBaseMonitoringPrototype : BarBaseMonitoringPrototype<MonitoringSensorMetainfo, BarSensorOptions>
    {
    }
}