namespace HSMDataCollector.SensorsMetainfo
{
    public enum Units
    {
        bits = 0,
        bytes = 1,
        KB = 2,
        MB = 3,
        GB = 4,

        Percents = 100,
    }


    internal class SensorMetainfoUnits
    {
        internal Units SelectedUnit { get; set; }

        internal Units[] AvailableUnits { get; set; }
    }
}