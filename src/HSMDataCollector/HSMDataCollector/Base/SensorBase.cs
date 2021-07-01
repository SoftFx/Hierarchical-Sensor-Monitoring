using HSMDataCollector.Core;
using HSMSensorDataObjects;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.Base
{
    public abstract class SensorBase : ISensor
    {
        protected readonly string Path;
        protected readonly string ProductKey;
        private readonly IValuesQueue _queue;

        protected SensorBase(string path, string productKey, IValuesQueue queue)
        {
            _queue = queue;
            Path = path;
            ProductKey = productKey;
        }
        public abstract bool HasLastValue { get; }
        public abstract CommonSensorValue GetLastValue();
        public abstract void Dispose();

        protected abstract string GetStringData(SensorValueBase data);
        protected void SendData(CommonSensorValue value)
        {
            _queue.Enqueue(value);
        }
    }
}