namespace HSMDataCollector.SensorsMetainfo
{
    internal static class SetEnables
    {
        internal static SensorMetainfoEnables ForGrafana { get; } = new SensorMetainfoEnables()
        {
            ForGrafana = true,
        };
    }


    internal class SensorMetainfoEnables
    {
        internal bool ForGrafana { get; set; }
    }
}