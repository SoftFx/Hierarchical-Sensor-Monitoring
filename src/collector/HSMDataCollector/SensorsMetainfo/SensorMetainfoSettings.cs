using System;

namespace HSMDataCollector.SensorsMetainfo
{
    internal class SensorMetainfoSettings
    {
        internal TimeSpan? KeepHistory { get; set; }

        internal TimeSpan? SelfDestroy { get; set; }

        internal TimeSpan? TTL { get; set; }
    }
}