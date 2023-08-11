using HSMSensorDataObjects;

namespace HSMDataCollector.SensorsMetainfo
{
    internal class SensorMetainfo
    {
        internal SensorMetainfoSettings Settings { get; set; } = new SensorMetainfoSettings();

        internal SensorMetainfoEnables Enables { get; set; } = new SensorMetainfoEnables();

        internal SensorMetainfoUnits Units { get; set; } = new SensorMetainfoUnits();


        internal SensorType SensorType { get; set; }

        internal string Description { get; set; }

        internal string Path { get; set; }


        internal bool OnlyUniqValues { get; set; }
    }
}