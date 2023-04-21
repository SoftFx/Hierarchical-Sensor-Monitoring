using HSMDataCollector.Options;

namespace HSMDataCollector.DefaultSensors.Other
{
    internal sealed class DataCollectorStatusSensor : SensorBase<string>
    {
        protected override string SensorName => "Collector statuses";


        public DataCollectorStatusSensor(SensorOptions options) : base(options)
        {
        }
    }
}