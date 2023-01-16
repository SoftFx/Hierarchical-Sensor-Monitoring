﻿using HSMDataCollector.Core;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.Base
{
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
            _queue.EnqueueData(value);
        }
    }
}