using HSMDataCollector.Options;
using HSMDataCollector.SensorsMetainfo;

namespace HSMDataCollector.Prototypes
{
    internal abstract class BarMonitoringPrototype : Prototype<MonitoringSensorMetainfo, BarSensorOptions>
    {
        protected const string SystemPath = ".Default";


        protected abstract string SensorName { get; }


        protected BarMonitoringPrototype()
        {
            Path = BuildPath(SystemPath, SensorName);
        }


        protected override MonitoringSensorMetainfo Apply(MonitoringSensorMetainfo info, BarSensorOptions options)
        {
            info.CollectBarPeriod = options.CollectBarPeriod;
            info.BarPeriod = options.BarPeriod;
            info.Precision = options.Precision;

            return info;
        }
    }
}