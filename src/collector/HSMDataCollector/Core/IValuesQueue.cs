using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.Core
{
    public interface IValuesQueue
    {
        void Enqueue(SensorValueBase value);
    }
}