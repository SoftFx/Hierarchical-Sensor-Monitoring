using HSMDataCollector.Options;
using HSMDataCollector.SensorsMetainfo;

namespace HSMDataCollector.Prototypes
{
    internal abstract class BarMonitoringPrototype : BaseMonitoringPrototype<BarMonitoringSensorMetainfo, BarSensorOptions>
    {
        protected override BarMonitoringSensorMetainfo Apply(BarMonitoringSensorMetainfo info, BarSensorOptions options)
        {
            info.CollectBarPeriod = options.CollectBarPeriod;
            info.BarPeriod = options.BarPeriod;
            info.Precision = options.Precision;

            return info;
        }
    }
}