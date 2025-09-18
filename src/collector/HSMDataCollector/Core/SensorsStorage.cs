using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Sensors;
using HSMDataCollector.SensorsFactory;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorRequests;


namespace HSMDataCollector.Core
{
    internal sealed class SensorsStorage : ConcurrentDictionary<string, ISensor>, IDisposable
    {
        private readonly CollectorOptions _options;

        private readonly DataProcessor _dataProcessor;

        private readonly PrototypesCollection _prototypes;

        public IWindowsCollection Windows { get; }

        public IUnixCollection Unix { get; }

        internal ICollectorLogger Logger { get; }


        internal SensorsStorage(CollectorOptions options, DataProcessor dataProcessor, ICollectorLogger logger)
        {
            _options      = options;
            _dataProcessor = dataProcessor;
            Logger        = logger;

            _prototypes = new PrototypesCollection(options, _dataProcessor);

            Windows = new WindowsSensorsCollection(this, _prototypes);
            Unix    = new UnixSensorsCollection(this, _prototypes);
        }


        public void Dispose()
        {
            foreach (var sensor in Values)
            {
                sensor.Dispose();
            }
        }

        internal Task InitAsync() => Task.WhenAll(Values.Select(async s => await s.InitAsync().ConfigureAwait(false)));

        internal Task StartAsync() => Task.WhenAll(Values.Select(async s => await s.StartAsync().ConfigureAwait(false)));

        internal Task StopAsync() => Task.WhenAll(Values.Select(async s => await s.StopAsync().ConfigureAwait(false)));


        internal MonitoringRateSensor CreateRateSensor(string path, RateSensorOptions options)
        {
            options = FillOptions<RateSensorOptions, RateDisplayUnit>(path, SensorType.RateSensor, options);

            return (MonitoringRateSensor)Register(new MonitoringRateSensor(options));
        }

        internal FileSensorInstant CreateFileSensor(string path, FileSensorOptions options)
        {
            options = FillOptions<FileSensorOptions, NoDisplayUnit>(path, SensorType.FileSensor, options);

            return (FileSensorInstant)Register(new FileSensorInstant(options, Logger));
        }


        internal FunctionSensorInstant<T> CreateFunctionSensor<T>(string path, Func<T> function, FunctionSensorOptions options)
        {
            options = FillOptions<FunctionSensorOptions, NoDisplayUnit>(path, SensorValuesFactory.GetInstantType<T>(), options);

            return (FunctionSensorInstant<T>)Register(new FunctionSensorInstant<T>(function, options));
        }

        internal ValuesFunctionSensorInstant<T, U> CreateValuesFunctionSensor<T, U>(string path, Func<List<U>, T> function, ValuesFunctionSensorOptions options)
        {
            options = FillOptions<ValuesFunctionSensorOptions, NoDisplayUnit>(path, SensorValuesFactory.GetInstantType<T>(), options);

            return (ValuesFunctionSensorInstant<T, U>)Register(new ValuesFunctionSensorInstant<T, U>(function, options));
        }


        internal LastValueSensorInstant<T> CreateLastValueSensor<T>(string path, T customDefault, InstantSensorOptions options)
        {
            options = FillOptions<InstantSensorOptions, NoDisplayUnit>(path, SensorValuesFactory.GetInstantType<T>(), options);

            return (LastValueSensorInstant<T>)Register(new LastValueSensorInstant<T>(options, customDefault));
        }

        internal SensorInstant<T, NoDisplayUnit> CreateInstantSensor<T>(string path, InstantSensorOptions options)
        {
            options = FillOptions<InstantSensorOptions, NoDisplayUnit>(path, SensorValuesFactory.GetInstantType<T>(), options);

            return (SensorInstant<T, NoDisplayUnit>)Register(new SensorInstant<T, NoDisplayUnit>(options));
        }

        internal SensorInstant<int, NoDisplayUnit> CreateEnumInstantSensor(string path, EnumSensorOptions options)
        {
            options = FillOptions<EnumSensorOptions, NoDisplayUnit>(path, SensorType.EnumSensor, options);

            return (SensorInstant<int, NoDisplayUnit>)Register(new SensorInstant<int, NoDisplayUnit>(options));
        }


        internal IntBarPublicSensor CreateIntBarSensor(string path, BarSensorOptions options)
        {
            options = FillOptions<BarSensorOptions, NoDisplayUnit>(path, SensorValuesFactory.GetBarType<int>(), options);

            return (IntBarPublicSensor)Register(new IntBarPublicSensor(options));
        }

        internal DoubleBarPublicSensor CreateDoubleBarSensor(string path, BarSensorOptions options)
        {
            options = FillOptions<BarSensorOptions, NoDisplayUnit>(path, SensorValuesFactory.GetBarType<double>(), options);

            return (DoubleBarPublicSensor)Register(new DoubleBarPublicSensor(options));
        }

        public IServiceCommandsSensor CreateServiceCommandsSensor()
        {
            return (IServiceCommandsSensor)Register(new ServiceCommandsSensor(_prototypes.ServiceCommands.Get(null)));
        }


        internal ISensor Register(ISensor sensor)
        {
            var path = sensor.SensorPath;

            if (TryGetValue(path, out var oldSensor))
                return oldSensor;

            if (_dataProcessor.IsStarted)
            {
                _ = AddAndStart(sensor);
                return sensor;
            }
            else
                return AddSensor(sensor);
        }


        private async Task<ISensor> AddAndStart(ISensor sensor)
        {
            if (!await AddSensor(sensor).InitAsync().ConfigureAwait(false))
                Logger.Error($"Failed to init {sensor.SensorPath}");
            else if (!await sensor.StartAsync().ConfigureAwait(false))
                Logger.Error($"Failed to start {sensor.SensorPath}");

            return sensor;
        }

        private ISensor AddSensor(ISensor sensor)
        {
            var path = sensor.SensorPath;

            if (TryAdd(path, sensor))
            {
                Logger.Info($"New sensor has been added {path}");

                return sensor;
            }

            throw new Exception($"Sensor with path {path} already exists");
        }

        private T FillOptions<T, TDisplayUnit>(string path, SensorType type, T options)
            where T : SensorOptions<TDisplayUnit>, new()
            where TDisplayUnit : struct, Enum
        {
            options = (T)options?.Copy() ?? new T();

            options.ComputerName = _options.ComputerName;
            options.Module = _options.Module;
            options.Path = path;
            options.Type = type;
            options.DataProcessor = _dataProcessor;

            return options;
        }

    }
}
