using HSMDataCollector.SensorsMetainfo;

namespace HSMDataCollector.Options
{
    internal sealed class SystemMonitoringPrototype : Prototype<MonitoringSensorMetainfo, BarSensorOptions>
    {
        protected override MonitoringSensorMetainfo BaseMetainfo { get; } = new MonitoringSensorMetainfo()
        {

        };


        protected override MonitoringSensorMetainfo Get(BarSensorOptions options)
        {
            throw new System.NotImplementedException();
        }
    }
}