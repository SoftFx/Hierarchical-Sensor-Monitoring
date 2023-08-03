using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.SensorsMetainfo
{
    internal static class SetUnits
    {
        internal static SensorMetainfoUnits SetPercents { get; } = new SensorMetainfoUnits(Unit.Percents);

        internal static SensorMetainfoUnits SetMB { get; } = new SensorMetainfoUnits(Unit.MB);
    }


    internal class SensorMetainfoUnits
    {
        internal Unit[] AvailableUnits { get; set; }

        internal Unit Selected { get; set; }


        internal SensorMetainfoUnits() { }

        internal SensorMetainfoUnits(Unit selected)
        {
            Selected = selected;
        }
    }
}