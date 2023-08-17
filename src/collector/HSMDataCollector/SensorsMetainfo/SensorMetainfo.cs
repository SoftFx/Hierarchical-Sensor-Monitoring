using HSMDataCollector.Alerts;
using HSMSensorDataObjects;
using System.Collections.Generic;

namespace HSMDataCollector.SensorsMetainfo
{
    internal class SensorMetainfo
    {
        internal List<AlertBuildRequest> Alerts { get; set; }

        internal SpecialAlertBuildRequest TtlAlert { get; set; }


        internal SensorMetainfoSettings Settings { get; set; } = new SensorMetainfoSettings();

        internal SensorMetainfoEnables Enables { get; set; } = new SensorMetainfoEnables();

        internal SensorMetainfoUnits Units { get; set; } = new SensorMetainfoUnits();


        internal SensorType SensorType { get; set; }

        internal string Description { get; set; }

        internal string Path { get; set; }


        internal bool OnlyUniqValues { get; set; }
    }
}