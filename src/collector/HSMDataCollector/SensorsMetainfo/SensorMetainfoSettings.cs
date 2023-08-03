using System;

namespace HSMDataCollector.SensorsMetainfo
{
    internal class SensorMetainfoSettings
    {
        internal TimeSpan TTL { get; set; }

        internal TimeSpan SaveSensorHistory { get; set; }

        internal TimeSpan SelfDestroy { get; set; }
    }
}