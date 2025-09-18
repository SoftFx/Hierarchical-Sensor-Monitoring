using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorRequests;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal class CollectorErrorsSensor : SensorBase<string, NoDisplayUnit>
    {
        public CollectorErrorsSensor(InstantSensorOptions options) : base(options) { }


        public void SendCollectorError(string error) => SendValue(error);
    }
}