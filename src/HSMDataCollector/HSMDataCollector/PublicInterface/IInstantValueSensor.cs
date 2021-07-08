using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    public interface IInstantValueSensor<T>
    {
        void AddValue(T value);
        void AddValue(T value, string comment = "");
        void AddValue(T value, SensorStatus status = SensorStatus.Unknown, string comment = "");
    }
}