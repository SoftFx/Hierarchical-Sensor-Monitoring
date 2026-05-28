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
using System.Threading;


namespace HSMDataCollector.Core
{
    /// <summary>
    /// Owns the set of registered sensors and orchestrates their lifecycle. Uses a private
    /// <see cref="ConcurrentDictionary{TKey, TValue}"/> for storage rather than inheriting from it,
    /// so the public surface is limited to the operations the collector actually needs.
    /// Sensor identity is the (full) sensor path.
    /// </summary>
    internal sealed class SensorsStorage : IDisposable
    {
        private readonly ConcurrentDictionary<string, ISensor> _sensors = new ConcurrentDictionary<string, ISensor>();

        private readonly CollectorOptions _options;

        private readonly DataProcessor _dataProcessor;

        private readonly PrototypesCollection _prototypes;
        private int _sensorCount;

        public IWindowsCollection Windows { get; }

        public IUnixCollection Unix { get; }

        internal ICollectorLogger Logger { get; }

        /// <summary>
        /// Snapshot of currently-registered sensors. Safe to enumerate during concurrent
        /// register/unregister; the underlying dictionary is concurrent.
        /// </summary>
        public IEnumerable<ISensor> Values => _sensors.Values;

        /// <summary>
        /// Approximate number of currently-registered sensors. Tracked via <see cref="Interlocked"/>
        /// for O(1) reads. Exposed for diagnostics — the cardinality cap is enforced internally
        /// using the increment result inside <see cref="AddSensor"/>, not via this property.
        /// May briefly disagree with the underlying dictionary count during concurrent registration.
        /// </summary>
        public int Count => Volatile.Read(ref _sensorCount);


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
            foreach (var sensor in _sensors.Values)
            {
                sensor.Dispose();
            }
        }

        internal Task InitAsync() => Task.WhenAll(_sensors.Values.Select(async s => await s.InitAsync().ConfigureAwait(false)));

        internal Task StartAsync() => Task.WhenAll(_sensors.Values.Select(async s => await s.StartAsync().ConfigureAwait(false)));

        internal Task StopAsync() => Task.WhenAll(_sensors.Values.Select(async s => await s.StopAsync().ConfigureAwait(false)));

        public bool TryRemove(string key, out ISensor value)
        {
            if (!_sensors.TryRemove(key, out value))
                return false;

            Interlocked.Decrement(ref _sensorCount);
            return true;
        }

        public bool TryGetValue(string key, out ISensor value) => _sensors.TryGetValue(key, out value);


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

            if (_sensors.TryGetValue(path, out var oldSensor))
                return oldSensor;

            if (_dataProcessor.CanStartNewSensors)
            {
                var addedSensor = AddSensor(sensor);
                _ = InitAndStart(addedSensor);
                return addedSensor;
            }
            else
                return AddSensor(sensor);
        }


        private async Task<ISensor> InitAndStart(ISensor sensor)
        {
            if (!await sensor.InitAsync().ConfigureAwait(false))
                Logger.Error($"Failed to init {sensor.SensorPath}");
            else if (!await sensor.StartAsync().ConfigureAwait(false))
                Logger.Error($"Failed to start {sensor.SensorPath}");

            return sensor;
        }

        private ISensor AddSensor(ISensor sensor)
        {
            var path = sensor.SensorPath;

            if (_sensors.TryAdd(path, sensor))
            {
                var count = Interlocked.Increment(ref _sensorCount);
                if (count > _options.MaxSensors)
                {
                    TryRemove(path, out _);
                    sensor.Dispose();
                    throw new InvalidOperationException($"Maximum sensor count {_options.MaxSensors} has been reached.");
                }

                Logger.Info($"New sensor has been added {path}");

                return sensor;
            }

            if (_sensors.TryGetValue(path, out var existingSensor))
            {
                sensor.Dispose();
                return existingSensor;
            }

            throw new InvalidOperationException($"Sensor with path {path} already exists");
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
