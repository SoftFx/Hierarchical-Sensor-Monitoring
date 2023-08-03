namespace HSMDataCollector.SensorsMetainfo
{
    internal class SensorMetainfo
    {
        internal SensorMetainfoSettings Settings { get; } = new SensorMetainfoSettings();

        internal SensorMetainfoEnables Enables { get; } = new SensorMetainfoEnables();

        internal SensorMetainfoUnits Units { get; } = new SensorMetainfoUnits();


        internal string Description { get; set; }

        internal string Path { get; set; }


        internal bool OnlyUniqValues { get; set; }
    }
}