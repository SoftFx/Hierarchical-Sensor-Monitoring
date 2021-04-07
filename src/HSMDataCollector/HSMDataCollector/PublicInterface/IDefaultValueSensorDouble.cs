using HSMSensorDataObjects;

namespace HSMDataCollector.PublicInterface
{
    public interface IDefaultValueSensorDouble
    {
        void AddValue(double value);
        void AddValue(double value, string comment);
        void AddValue(double value, SensorStatus status, string comment = null);
    }
}