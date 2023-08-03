using System;

namespace HSMDataCollector.SensorsMetainfo
{
    internal class MonitoringSensorMetainfo : SensorMetainfo
    {
        public TimeSpan CollectBarPeriod { get; set; } = TimeSpan.FromSeconds(5);

        public TimeSpan BarPeriod { get; set; } = TimeSpan.FromMinutes(5);

        public int Precision { get; set; } = 2;
    }
}