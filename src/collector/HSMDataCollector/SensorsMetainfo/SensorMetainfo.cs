using HSMDataCollector.Alerts;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;
using System.Collections.Generic;

namespace HSMDataCollector.SensorsMetainfo
{
    internal class SensorMetainfo
    {
        internal List<AlertBaseTemplate> Alerts { get; set; }

        internal SpecialAlertTemplate TtlAlert { get; set; }


        internal SensorMetainfoSettings Settings { get; set; } = new SensorMetainfoSettings();

        internal SensorMetainfoEnables Enables { get; set; } = new SensorMetainfoEnables();


        internal SensorType SensorType { get; set; }

        internal Unit? OriginalUnit { get; set; }


        internal string Description { get; set; }

        internal string Path { get; set; }


        internal bool OnlyUniqValues { get; set; }
    }
}