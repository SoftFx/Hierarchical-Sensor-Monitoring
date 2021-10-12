using HSMDataCollector.Core;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.Base
{
    public abstract class SensorBase : ISensor
    {
        protected readonly string Path;
        protected readonly string ProductKey;
        protected readonly string Description;
        private readonly IValuesQueue _queue;
        protected SensorBase(string path, string productKey, IValuesQueue queue, string description)
        {
            _queue = queue;
            Path = path;
            ProductKey = productKey;
            Description = description;
        }
        public abstract bool HasLastValue { get; }
        public abstract UnitedSensorValue GetLastValue();
        public abstract void Dispose();

        protected void EnqueueValue(UnitedSensorValue value)
        {
            _queue.EnqueueData(value);
        }
    }
}