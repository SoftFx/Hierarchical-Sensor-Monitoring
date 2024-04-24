using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal class CollectorErrorsSensor : SensorBase<string>
    {
        public CollectorErrorsSensor(SensorOptions options) : base(options) { }


        public void SendCollectorError(string error) => SendValue(error);
    }
}