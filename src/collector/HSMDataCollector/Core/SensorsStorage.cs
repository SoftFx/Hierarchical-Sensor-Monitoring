using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Logging;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace HSMDataCollector.Core
{
    internal sealed class SensorsStorage : ConcurrentDictionary<string, SensorBase>, IDisposable
    {
        private readonly IValuesQueue _valuesQueue;
        private readonly ICollectorLogger _logger;


        internal SensorsStorage(IValuesQueue queue, ICollectorLogger logger)
        {
            _valuesQueue = queue;
            _logger = logger;
        }


        public void Dispose()
        {
            foreach (var value in Values)
            {
                value.ReceiveSensorValue -= _valuesQueue.Push;
                value.ExceptionThrowing -= WriteSensorException;

                value.Dispose();
            }
        }

        internal Task Init() => Task.WhenAll(Values.Select(s => s.Init()));

        internal Task Start() => Task.WhenAll(Values.Select(s => s.Start()));

        internal Task Stop() => Task.WhenAll(Values.Select(s => s.Stop()));

        internal void Register(string key, SensorBase value)
        {
            if (TryAdd(key, value))
            {
                value.ReceiveSensorValue += _valuesQueue.Push;
                value.ExceptionThrowing += WriteSensorException;

                _logger?.Info($"Added new default sensor {key}");
            }
        }


        private void WriteSensorException(string sensorPath, Exception ex)
        {
            _logger?.Error($"Sensor: {sensorPath}, {ex}");
        }
    }
}
