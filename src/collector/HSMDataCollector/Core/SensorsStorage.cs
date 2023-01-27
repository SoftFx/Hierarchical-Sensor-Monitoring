using HSMDataCollector.DefaultSensors;
using System;
using System.Collections.Concurrent;

namespace HSMDataCollector.Core
{
    internal sealed class SensorsStorage : ConcurrentDictionary<string, MonitoringSensorBase>, IDisposable
    {
        private readonly IValuesQueue _valuesQueue;


        internal SensorsStorage(IValuesQueue queue)
        {
            _valuesQueue = queue;
        }


        public void Dispose()
        {
            foreach (var sensor in Values)
                sensor.Dispose();
        }

        internal void Register(string key, MonitoringSensorBase value)
        {
            if (TryAdd(key, value))
            {
                value.ReceiveSensorValue += _valuesQueue.EnqueueData;

                value.Start();

                //_logger?.Info($"Added new sensor {key}");
            }
        }
    }
}
