using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    public interface IDefaultValueSensorInt
    {
        void AddValue(int value);
        void AddValue(int value, string comment);
        void AddValue(int value, SensorStatus status, string comment = null);
    }
}