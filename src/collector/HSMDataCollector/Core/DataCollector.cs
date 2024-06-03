using HSMDataCollector.Client;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Extensions;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.Prototypes;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace HSMDataCollector.Core
{
    public enum CollectorStatus : byte
    {
        Starting = 0,
        Running,
        Stopping,
        Stopped,
    }


    public sealed class DataCollector : IDataCollector
    {
        private readonly LoggerManager _logger = new LoggerManager();

        private readonly PrototypesCollection _prototypes;
        private readonly SensorsStorage _sensorsStorage;
        private readonly IQueueManager _queueManager;
        private readonly CollectorOptions _options;
        private readonly HsmHttpsClient _hsmClient;


        internal static bool IsWindowsOS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private DefaultSensorsCollection CurrentCollection => IsWindowsOS ? (DefaultSensorsCollection)Windows : (DefaultSensorsCollection)Unix;


        public IWindowsCollection Windows { get; }

        public IUnixCollection Unix { get; }


        public CollectorStatus Status { get; private set; } = CollectorStatus.Stopped;

        public string ComputerName => _options?.ComputerName;

        public string Module => _options?.Module;


        public event Action ToStarting;
        public event Action ToRunning;
        public event Action ToStopping;
        public event Action ToStopped;


        [Obsolete]
        public event EventHandler ValuesQueueOverflow;


        /// <summary>
        /// Creates new instance of <see cref="DataCollector"/> class, initializing main parameters
        /// </summary>
        /// <param name="options">Common options for datacollector</param>
        public DataCollector(CollectorOptions options)
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(UnhandledExceptionHandler);

            _options = options;

            _queueManager = new QueueManager(options, _logger);
            _sensorsStorage = new SensorsStorage(this, _queueManager, _logger);
            _prototypes = new PrototypesCollection(options);

            Windows = new WindowsSensorsCollection(_sensorsStorage, _prototypes);
            Unix = new UnixSensorsCollection(_sensorsStorage, _prototypes);

            _hsmClient = new HsmHttpsClient(options, _queueManager, _logger);

            ToRunning += ToRunningCollector;
            ToStopped += ToStoppedCollector;
        }

        /// <summary>
        /// Creates new instance of <see cref="DataCollector"/> class, initializing main parameters
        /// </summary>
        /// <param name="productKey">Key, which identifies the product (logical group) for all sensors that will be created.</param>
        /// <param name="address">HSM server address to send data to (Do not forget https:// if needed)</param>
        /// <param name="port">HSM sensors API port, which defaults to 44330. Specify if your HSM server Docker container configured differently.</param>
        public DataCollector(string productKey, string address = CollectorOptions.LocalhostAddress, int port = CollectorOptions.DefaultPort, string clientName = null)
            : this(new CollectorOptions()
            {
                AccessKey = productKey,
                ServerAddress = address,
                Port = port,
                ClientName = clientName
            })
        { }


        public Task<ConnectionResult> TestConnection() => _hsmClient.TestConnection();

        public IDataCollector AddNLog(LoggerOptions options = null)
        {
            _logger.AddLogger(new NLogLogger(options));

            return this;
        }

        public IDataCollector AddCustomLogger(ICollectorLogger logger)
        {
            _logger.AddLogger(logger);

            return this;
        }

        [Obsolete("Use method AddNLog() to add logging and method Start() after default sensors initialization")]
        public void Initialize(bool useLogging = true, string folderPath = null, string fileNameFormat = null)
        {
            if (useLogging)
                AddNLog();

            _logger.Info("Initialize timer...");
            _queueManager.Init();
        }

        [Obsolete("Use Initialize(bool, string, string)")]
        public void Initialize()
        {
            _queueManager.Init();
        }


        public Task Start() => Start(Task.CompletedTask);

        public async Task Start(Task customStartingTask)
        {
            try
            {
                if (!Status.IsStopped())
                    return;

                _queueManager.Init();

                ChangeStatus(CollectorStatus.Starting);

                await customStartingTask;

                _ = _sensorsStorage.Init().ContinueWith(_ => ChangeStatus(CollectorStatus.Running));
            }
            catch (Exception ex)
            {
                _logger.Error(ex);

                ChangeStatus(CollectorStatus.Stopped);
            }
        }


        public Task Stop() => Stop(Task.CompletedTask);

        public async Task Stop(Task customStartingTask)
        {
            try
            {
                if (!Status.IsRunning())
                    return;

                ChangeStatus(CollectorStatus.Stopping);

                await Task.WhenAll(_sensorsStorage.Stop(), customStartingTask);

                ChangeStatus(CollectorStatus.Stopped);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);

                ChangeStatus(CollectorStatus.Stopped);
            }
            finally
            {
                _queueManager.Stop();
            }
        }

        public void Dispose()
        {
            if (!Status.IsRunning())
                return;

            ChangeStatus(CollectorStatus.Stopping);

            _sensorsStorage.Dispose();

            ChangeStatus(CollectorStatus.Stopped);

            ToRunning -= ToRunningCollector;
            ToStopped -= ToStoppedCollector;

            CurrentCollection?.Dispose();

            _queueManager.Dispose();
            _hsmClient.Dispose();
        }


        private void ChangeStatus(CollectorStatus newStatus)
        {
            Status = newStatus;

            _logger.Info($"DataCollector (v. {DataCollectorExtensions.Version}) -> {newStatus}");

            switch (newStatus)
            {
                case CollectorStatus.Starting:
                    ToStarting?.Invoke();
                    break;
                case CollectorStatus.Running:
                    ToRunning?.Invoke();
                    break;
                case CollectorStatus.Stopping:
                    ToStopping?.Invoke();
                    break;
                case CollectorStatus.Stopped:
                    ToStopped?.Invoke();
                    break;
            }
        }

        private void ToRunningCollector() => _ = _sensorsStorage.Start();

        private void ToStoppedCollector() => _queueManager.Stop();

        private void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            _logger.Error($"Unhandled exception caught: {ex.Message}");
        }

        #region Obsolets

        [Obsolete("Use method AddSystemMonitoringSensors(options) in Windows collection")]
        public void InitializeSystemMonitoring(bool isCPU, bool isFreeRam, string specificPath = null)
        {
            if (IsWindowsOS)
            {
                var options = new BarSensorOptions() { Path = specificPath };
                //var options = _sensorsPrototype.SystemMonitoring.GetAndFill(new BarSensorOptions() { NodePath = specificPath });

                if (isCPU)
                    Windows.AddTotalCpu(options);
                if (isFreeRam)
                    Windows.AddFreeRamMemory(options);
            }

            _ = Start();
        }

        [Obsolete("Use method AddProcessSensors(options) in Windows or Unix collections")]
        public void InitializeProcessMonitoring(bool isCPU, bool isMemory, bool isThreads, string specificPath = null)
        {
            var options = new BarSensorOptions() { Path = specificPath };
            //var options = _sensorsPrototype.ProcessMonitoring.GetAndFill(new BarSensorOptions() { NodePath = specificPath });

            if (IsWindowsOS)
            {
                if (isCPU)
                    Windows.AddProcessCpu(options);
                if (isMemory)
                    Windows.AddProcessMemory(options);
                if (isThreads)
                    Windows.AddProcessThreadCount(options);
            }
            else
            {
                if (isCPU)
                    Unix.AddProcessCpu(options);
                if (isMemory)
                    Unix.AddProcessMemory(options);
                if (isThreads)
                    Unix.AddProcessThreadCount(options);
            }

            _ = Start();
        }

        [Obsolete("Method has no implementation")]
        public void InitializeProcessMonitoring(string processName, bool isCPU, bool isMemory, bool isThreads, string specificPath = null)
        {

        }

        [Obsolete("Use method AddWindowsSensors(options) in Windows collection")]
        public void InitializeOsMonitoring(bool isUpdated, string specificPath = null)
        {
            if (isUpdated)
                InitializeWindowsUpdateMonitoring(TimeSpan.FromHours(12), TimeSpan.FromDays(30), specificPath);
        }

        [Obsolete("Use method AddCollectorAlive(options) in Windows or Unix collections")]
        public void MonitorServiceAlive(string specificPath = null)
        {
            //var options = _sensorsPrototype.CollectorAlive.GetAndFill(new CollectorMonitoringInfoOptions() { NodePath = specificPath });
            var options = new CollectorMonitoringInfoOptions() { Path = specificPath };

            if (IsWindowsOS)
                Windows.AddCollectorAlive(options);
            else
                Unix.AddCollectorAlive(options);

            _ = Start();
        }

        [Obsolete("Use method AddWindowsSensors(options) in Windows collection")]
        public bool InitializeWindowsUpdateMonitoring(TimeSpan sensorInterval, TimeSpan updateInterval, string specificPath = null)
        {
            return true;
        }

        #endregion


        #region Custom instant sensors

        public IInstantValueSensor<Version> CreateVersionSensor(string path, string description = "") => CreateInstantSensor<Version>(path, description);

        public IInstantValueSensor<TimeSpan> CreateTimeSensor(string path, string description = "") => CreateInstantSensor<TimeSpan>(path, description);

        public IInstantValueSensor<double> CreateDoubleSensor(string path, string description = "") => CreateInstantSensor<double>(path, description);

        public IInstantValueSensor<string> CreateStringSensor(string path, string description = "") => CreateInstantSensor<string>(path, description);

        public IInstantValueSensor<bool> CreateBoolSensor(string path, string description = "") => CreateInstantSensor<bool>(path, description);

        public IInstantValueSensor<int> CreateIntSensor(string path, string description = "") => CreateInstantSensor<int>(path, description);


        public IInstantValueSensor<Version> CreateVersionSensor(string path, InstantSensorOptions options) => CreateInstantSensor<Version>(path, options);

        public IInstantValueSensor<TimeSpan> CreateTimeSensor(string path, InstantSensorOptions options) => CreateInstantSensor<TimeSpan>(path, options);

        public IInstantValueSensor<double> CreateDoubleSensor(string path, InstantSensorOptions options) => CreateInstantSensor<double>(path, options);

        public IInstantValueSensor<string> CreateStringSensor(string path, InstantSensorOptions options) => CreateInstantSensor<string>(path, options);

        public IInstantValueSensor<bool> CreateBoolSensor(string path, InstantSensorOptions options) => CreateInstantSensor<bool>(path, options);

        public IInstantValueSensor<int> CreateIntSensor(string path, InstantSensorOptions options) => CreateInstantSensor<int>(path, options);


        private IInstantValueSensor<T> CreateInstantSensor<T>(string path, string description) =>
            CreateInstantSensor<T>(path, new InstantSensorOptions()
            {
                Description = description,
            });

        private IInstantValueSensor<T> CreateInstantSensor<T>(string path, InstantSensorOptions options) => _sensorsStorage.CreateInstantSensor<T>(path, options);


        public IServiceCommandsSensor CreateServiceCommandsSensor()
        {
            return (IServiceCommandsSensor)_sensorsStorage.Register(new ServiceCommandsSensor(_prototypes.ServiceCommands.Get(null)));
        }


        public IMonitoringRateSensor CreateM1RateSensor(string path, string desctiption = "") => CreateRateSensor(path, 60000, desctiption);

        public IMonitoringRateSensor CreateM5RateSensor(string path, string description = "") => CreateRateSensor(path, 300000, description);

        public IMonitoringRateSensor CreateRateSensor(string path, RateSensorOptions options) => _sensorsStorage.CreateRateSensor(path, options);

        private IMonitoringRateSensor CreateRateSensor(string path, int postPeriod, string description = "") => CreateRateSensor(path, new RateSensorOptions
        {
            PostDataPeriod = TimeSpan.FromMilliseconds(postPeriod),
            Description = description,
        });


        #endregion


        #region File sensors

        public IFileSensor CreateFileSensor(string path, string fileName, string extension = "txt", string description = "") =>
            CreateFileSensor(path, new FileSensorOptions()
            {
                DefaultFileName = fileName,
                Description = description,
                Extension = extension,
            });

        public IFileSensor CreateFileSensor(string path, FileSensorOptions options) => _sensorsStorage.CreateFileSensor(path, options);

        public Task<bool> SendFileAsync(string sensorPath, string filePath, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            var fullSensorPath = DefaultPrototype.BuildPath(_options.ComputerName, _options.Module, sensorPath);
            var sensor = _sensorsStorage.CreateFileSensor(fullSensorPath, new FileSensorOptions());

            return sensor.SendFile(filePath, status, comment);
        }

        #endregion


        #region Last value sensors

        public ILastValueSensor<double> CreateLastValueDoubleSensor(string path, double defaultValue, string description = "") => CreateLastValueSensor(path, defaultValue, description);

        public ILastValueSensor<string> CreateLastValueStringSensor(string path, string defaultValue, string description = "") => CreateLastValueSensor(path, defaultValue, description);

        public ILastValueSensor<bool> CreateLastValueBoolSensor(string path, bool defaultValue, string description = "") => CreateLastValueSensor(path, defaultValue, description);

        public ILastValueSensor<int> CreateLastValueIntSensor(string path, int defaultValue, string description = "") => CreateLastValueSensor(path, defaultValue, description);

        public ILastValueSensor<Version> CreateLastValueVersionSensor(string path, Version defaultValue, string description = "") => CreateLastValueSensor(path, defaultValue, description);

        public ILastValueSensor<TimeSpan> CreateLastValueTimeSpanSensor(string path, TimeSpan defaultValue, string description = "") => CreateLastValueSensor(path, defaultValue, description);

        public ILastValueSensor<T> CreateLastValueSensor<T>(string path, InstantSensorOptions options, T defaultValue = default) => _sensorsStorage.CreateLastValueSensor(path, defaultValue, options);

        private ILastValueSensor<T> CreateLastValueSensor<T>(string path, T defaultValue, string description = "") =>
            CreateLastValueSensor(path, new InstantSensorOptions()
            {
                Description = description,
            }, defaultValue);

        #endregion


        #region Generic bar sensors

        public IBarSensor<int> Create1HrIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 3600000, 15000, description);

        public IBarSensor<int> Create30MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 1800000, 15000, description);

        public IBarSensor<int> Create10MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 600000, 15000, description);

        public IBarSensor<int> Create5MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 300000, 15000, description);

        public IBarSensor<int> Create1MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 60000, 15000, description);

        public IBarSensor<int> CreateIntBarSensor(string path, int barPeriod, int postPeriod = 15000, string description = "") =>
            CreateIntBarSensor(path, BuildBarOptions(barPeriod, postPeriod, description));

        public IBarSensor<int> CreateIntBarSensor(string path, BarSensorOptions options) => _sensorsStorage.CreateIntBarSensor(path, options);


        public IBarSensor<double> Create1HrDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 3600000, 15000, precision, description);

        public IBarSensor<double> Create30MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 1800000, 15000, precision, description);

        public IBarSensor<double> Create10MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 600000, 15000, precision, description);

        public IBarSensor<double> Create5MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 300000, 15000, precision, description);

        public IBarSensor<double> Create1MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 60000, 15000, precision, description);

        public IBarSensor<double> CreateDoubleBarSensor(string path, int barPeriod, int postPeriod, int precision = 2, string description = "") =>
            CreateDoubleBarSensor(path, BuildBarOptions(barPeriod, postPeriod, description, precision));

        public IBarSensor<double> CreateDoubleBarSensor(string path, BarSensorOptions options) => _sensorsStorage.CreateDoubleBarSensor(path, options);


        private BarSensorOptions BuildBarOptions(int barPeriod, int postPeriod, string description, int precision = 2) =>
            new BarSensorOptions()
            {
                PostDataPeriod = TimeSpan.FromMilliseconds(postPeriod),
                BarPeriod = TimeSpan.FromMilliseconds(barPeriod),

                Description = description,
                Precision = precision,
            };

        #endregion

        #region Generic func sensors

        public INoParamsFuncSensor<T> CreateNoParamsFuncSensor<T>(string path, string description, Func<T> function, int millisecondsInterval) =>
            CreateNoParamsFuncSensor(path, description, function, TimeSpan.FromMilliseconds(millisecondsInterval));

        public INoParamsFuncSensor<T> Create1MinNoParamsFuncSensor<T>(string path, string description, Func<T> function) =>
            CreateNoParamsFuncSensor(path, description, function, TimeSpan.FromMilliseconds(60000));

        public INoParamsFuncSensor<T> Create5MinNoParamsFuncSensor<T>(string path, string description, Func<T> function) =>
            CreateNoParamsFuncSensor(path, description, function, TimeSpan.FromMilliseconds(300000));

        public INoParamsFuncSensor<T> CreateFunctionSensor<T>(string path, Func<T> function, FunctionSensorOptions options) =>
            _sensorsStorage.CreateFunctionSensor(path, function, options);

        public INoParamsFuncSensor<T> CreateNoParamsFuncSensor<T>(string path, string description, Func<T> function, TimeSpan interval) =>
            CreateFunctionSensor(path, function, new FunctionSensorOptions()
            {
                PostDataPeriod = interval,
                Description = description,
            });


        public IParamsFuncSensor<T, U> CreateParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function, int millisecondsInterval) =>
            CreateParamsFuncSensor(path, description, function, TimeSpan.FromMilliseconds(millisecondsInterval));

        public IParamsFuncSensor<T, U> Create1MinParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function) =>
            CreateParamsFuncSensor(path, description, function, TimeSpan.FromMilliseconds(60000));

        public IParamsFuncSensor<T, U> Create5MinParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function) =>
            CreateParamsFuncSensor(path, description, function, TimeSpan.FromMilliseconds(300000));

        public IParamsFuncSensor<T, U> CreateValuesFunctionSensor<T, U>(string path, Func<List<U>, T> function, ValuesFunctionSensorOptions options) =>
            _sensorsStorage.CreateValuesFunctionSensor(path, function, options);

        public IParamsFuncSensor<T, U> CreateParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function, TimeSpan interval) =>
            CreateValuesFunctionSensor(path, function, new ValuesFunctionSensorOptions()
            {
                PostDataPeriod = interval,
                Description = description,
            });

        #endregion
    }
}