using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.Core
{
    public interface IValuesQueue
    {
        void EnqueueData(SensorValueBase value);
    }
}