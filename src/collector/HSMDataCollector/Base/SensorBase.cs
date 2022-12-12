using HSMDataCollector.Core;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.Base
{
    public abstract class SensorBase : ISensor
    {
        protected readonly string ProductKey;
        protected readonly string Description;
        private readonly IValuesQueue _queue;

        internal string Path { get; }


        protected SensorBase(string path, string productKey, IValuesQueue queue, string description)
        {
            _queue = queue;
            Path = path;
            ProductKey = productKey;
            Description = description;
        }
        public abstract bool HasLastValue { get; }
        public abstract SensorValueBase GetLastValue();
        public abstract void Dispose();

        protected void EnqueueValue(SensorValueBase value)
        {
            _queue.EnqueueData(value);
        }
    }
}