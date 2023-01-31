using HSMDataCollector.DefaultSensors;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

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
            foreach (var value in Values)
            {
                value.ReceiveSensorValue -= _valuesQueue.EnqueueData;

                value.Dispose();
            }
        }

        internal Task Start() => Task.WhenAll(Values.Select(s => s.Start()));

        internal void Register(string key, MonitoringSensorBase value)
        {
            if (TryAdd(key, value))
            {
                value.ReceiveSensorValue += _valuesQueue.EnqueueData;

                //_logger?.Info($"Added new sensor {key}");
            }
        }
    }
}
