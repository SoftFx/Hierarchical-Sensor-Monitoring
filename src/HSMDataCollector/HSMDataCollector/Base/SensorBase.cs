using System;
using HSMDataCollector.Core;
using HSMSensorDataObjects;
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
        public abstract CommonSensorValue GetLastValue();
        public abstract UnitedSensorValue GetLastValueNew();
        public abstract void Dispose();
        protected abstract string GetStringData(SensorValueBase data);
        [Obsolete("07.07.2021. Use another data object.")]
        protected void EnqueueData(CommonSensorValue value)
        {
            _queue.Enqueue(value);
        }

        protected void EnqueueValue(UnitedSensorValue value)
        {
            _queue.EnqueueData(value);
        }
    }
}