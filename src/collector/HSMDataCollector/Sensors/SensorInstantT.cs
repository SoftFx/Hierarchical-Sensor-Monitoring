using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;

namespace HSMDataCollector.Sensors
{
    internal class SensorInstant<T> : SensorBase<T>, IInstantValueSensor<T>
    {
        public SensorInstant(SensorOptions options) : base(options) { }


        public void AddValue(T value) => SendValue(value);

        public void AddValue(T value, string comment = "") => SendValue(value, comment: comment);

        public void AddValue(T value, SensorStatus status = SensorStatus.Ok, string comment = "") => SendValue(value, status, comment);
    }
}