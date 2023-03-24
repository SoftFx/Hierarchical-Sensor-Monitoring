using System;
using HSMDataCollector.Core;
using HSMDataCollector.Extensions;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.Base
{
    [Obsolete("Will be replaced by ./SensorBases/SensorBase.cs")]
    public abstract class SensorBase : ISensor
    {
        private readonly IValuesQueue _queue;

        protected string Description { get; }

        internal string Path { get; }

        public abstract bool HasLastValue { get; }


        protected SensorBase(string path, IValuesQueue queue, string description)
        {
            _queue = queue;
            Path = path;
            Description = description;
        }


        public abstract void Dispose();

        public abstract SensorValueBase GetLastValue();

        protected void EnqueueValue(SensorValueBase value)
        {
            _queue.Enqueue(value.TrimLongComment());
        }
    }
}