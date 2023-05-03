using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.Core
{
    public interface IValuesQueue
    {
        void Push(SensorValueBase value);
    }
}