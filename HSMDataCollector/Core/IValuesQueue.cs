using HSMSensorDataObjects;

namespace HSMDataCollector.Core
{
    public interface IValuesQueue
    {
        void Enqueue(CommonSensorValue value);
    }
}