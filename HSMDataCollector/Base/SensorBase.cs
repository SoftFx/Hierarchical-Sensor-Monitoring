using HSMDataCollector.Core;
using HSMDSensorDataObjects;
using HSMSensorDataObjects;

namespace HSMDataCollector.Base
{
    public abstract class SensorBase
    {
        protected readonly string Path;
        protected readonly string ProductKey;
        protected readonly string ServerAddress;
        private readonly IValuesQueue _queue;
        protected SensorBase(string path, string productKey, string serverAddress, IValuesQueue queue)
        {
            _queue = queue;
            Path = path;
            ProductKey = productKey;
            ServerAddress = serverAddress;
        }

        protected abstract byte[] GetBytesData(SensorValueBase data);
        protected abstract string GetStringData(SensorValueBase data);
        protected void SendData(CommonSensorValue value)
        {
            _queue.Enqueue(value);
        }
    }
}