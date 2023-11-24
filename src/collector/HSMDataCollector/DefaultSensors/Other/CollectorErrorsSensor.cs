using HSMDataCollector.Options;
using HSMSensorDataObjects;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal class CollectorErrorsSensor : SensorBase<string>
    {
        public CollectorErrorsSensor(SensorOptions options) : base(options) { }


        public void SendCollectorError(string error) => SendValue(error, SensorStatus.Error);
    }
}