using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Extensions;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.Sensors;
using HSMDataCollector.SensorsFactory;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;

namespace HSMDataCollector.Core
{
    internal sealed class SensorsStorage : ConcurrentDictionary<string, SensorBase>, IDisposable
    {
        private readonly IDataCollector _collector;


        internal IQueueManager QueueManager { get; }

        internal ILoggerManager Logger { get; }


        internal SensorsStorage(IDataCollector collector, IQueueManager queue, ILoggerManager logger)
        {
            _collector = collector;

            QueueManager = queue;
            Logger = logger;
        }


        public void Dispose()
        {
            foreach (var value in Values)
            {
                value.SensorCommandRequest -= QueueManager.Commands.CallServer;
                value.ReceiveSensorValue -= QueueManager.Data.Push;
                value.ExceptionThrowing -= WriteSensorException;

                value.Dispose();
            }
        }

        internal Task Init() => Task.WhenAll(Values.Select(s => s.Init()));

        internal Task Start() => Task.WhenAll(Values.Select(s => s.Start()));

        internal Task Stop() => Task.WhenAll(Values.Select(s => s.Stop()));


        internal SensorInstant<T> CreateInstantSensor<T>(string path, InstantSensorOptions options)
        {
            options = FillOptions(path, SensorValuesFactory.GetInstantType<T>(), options);

            return (SensorInstant<T>)Register(new SensorInstant<T>(options));
        }

        internal IntBarPublicSensor CreateIntBarSensor(string path, BarSensorOptions options)
        {
            options = FillOptions(path, SensorValuesFactory.GetBarType<int>(), options);

            return (IntBarPublicSensor)Register(new IntBarPublicSensor(options));
        }

        internal DoubleBarPublicSensor CreateDoubleBarSensor(string path, BarSensorOptions options)
        {
            options = FillOptions(path, SensorValuesFactory.GetBarType<double>(), options);

            return (DoubleBarPublicSensor)Register(new DoubleBarPublicSensor(options));
        }


        internal SensorBase Register(SensorBase sensor)
        {
            var path = sensor.SensorPath;

            if (TryGetValue(path, out var oldSensor))
                return oldSensor;

            if (_collector.Status.IsRunning())
            {
                _ = AddAndStart(sensor);
                return sensor;
            }

            return AddSensor(sensor);
        }


        private async Task<SensorBase> AddAndStart(SensorBase sensor)
        {
            var path = sensor.SensorPath;

            if (!await AddSensor(sensor).Init())
                Logger.Error($"Failed to init {path}");
            else if (!await sensor.Start())
                Logger.Error($"Failed to start {path}");

            return sensor;
        }

        private SensorBase AddSensor(SensorBase sensor)
        {
            var path = sensor.SensorPath;

            if (TryAdd(path, sensor))
            {
                sensor.SensorCommandRequest += QueueManager.Commands.CallServer;
                sensor.ReceiveSensorValue += QueueManager.Data.Push;
                sensor.ExceptionThrowing += WriteSensorException;

                Logger.Info($"New sensor has been added {path}");

                return sensor;
            }

            throw new Exception($"Sensor with path {path} already exists");
        }

        private T FillOptions<T>(string path, SensorType type, T options) where T : SensorOptions
        {
            options.ComputerName = _collector.ComputerName;
            options.Module = _collector.Module;
            options.Path = path;
            options.Type = type;

            return options;
        }

        private void WriteSensorException(string sensorPath, Exception ex) => Logger.Error($"Sensor: {sensorPath}, {ex}");
    }
}
