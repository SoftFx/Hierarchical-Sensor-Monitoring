using HSMDataCollector.DefaultSensors;
using System.Collections.Concurrent;

namespace HSMDataCollector.Core
{
    internal sealed class SensorsStorage : ConcurrentDictionary<string, MonitoringSensorBase>
    {
        private readonly IValuesQueue _valuesQueue;


        internal SensorsStorage(IValuesQueue queue)
        {
            _valuesQueue = queue;
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
