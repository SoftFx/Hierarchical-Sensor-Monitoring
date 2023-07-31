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


        internal async Task<SensorBase> Run(SensorBase sensor)
        {
            var path = sensor.SensorPath;

            if (!await Register(sensor).Init())
                _logger.Error($"Failed to init {path}");
            else if (!await sensor.Start())
                _logger.Error($"Failed to start {path}");

            return sensor;
        }

        internal SensorBase Register(SensorBase sensor)
        {
            if (TryAdd(sensor.SensorPath, sensor))
            {
                sensor.ReceiveSensorValue += _valuesQueue.Push;
                sensor.ExceptionThrowing += WriteSensorException;

                _logger.Info($"New sensor has been added {sensor.SensorPath}");

                return sensor;
            }

            throw new Exception($"Sensor with path {sensor.SensorPath} already exists");
        }


        private void WriteSensorException(string sensorPath, Exception ex) => _logger.Error($"Sensor: {sensorPath}, {ex}");
    }
}
