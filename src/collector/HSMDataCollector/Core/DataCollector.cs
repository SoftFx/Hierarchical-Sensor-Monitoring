using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using HSMDataCollector.Client;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.Extensions;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.Prototypes;
using HSMDataCollector.PublicInterface;
using HSMDataCollector.Threading;
using HSMSensorDataObjects;


namespace HSMDataCollector.Core
{
    public enum CollectorStatus : byte
    {
        Starting = 0,
        Running,
        Stopping,
        Stopped,
        Disposed,
    }


    public sealed class DataCollector : IDataCollector, ICollectorRegistrationState, ILifecycleObservableCollector
    {
        private readonly LoggerManager _logger = new LoggerManager();

        private readonly SensorsStorage _sensorsStorage;
        private readonly CollectorOptions _options;
        private readonly IDataSender _dataSender;
        private readonly DataProcessor _dataProcessor;
        private readonly CollectorLifecycle _lifecycle = new CollectorLifecycle();
        private readonly ICollectorScheduler _scheduler;

        // Serializes lifecycle state transitions with their lifecycle event raise so that
        // concurrent Start/Stop/Dispose cannot reorder events (e.g. ToStopping before ToStarting).
        // Always acquired before any _lifecycle method that mutates state.
        private readonly object _opLock = new object();

        // Tracks the in-flight SensorStorage init/start phase so Stop/Dispose do not dispose
        // queues or sensors while Start() is still touching them.
        private Task _currentStartInitTask;

        // Tracks the in-flight processor StopAsync task so that a racing Dispose() can wait for it
        // instead of issuing a duplicate StopAsync (which would no-op against queues already in Stopping
        // and then fire ToStopped while the original Stop is still draining).
        private Task _currentProcessorStopTask;

        // Observer-pattern lifecycle listeners (portable alternative to the C# events). Guarded by
        // its own lock; notified from LogAndRaise alongside the events.
        private readonly object _listenersLock = new object();
        private readonly List<ILifecycleListener> _lifecycleListeners = new List<ILifecycleListener>();

        internal static bool IsWindowsOS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        public IWindowsCollection Windows => _sensorsStorage.Windows;

        public IUnixCollection Unix => _sensorsStorage.Unix;


        public CollectorStatus Status => _lifecycle.Status;

        public bool IsAcceptingRegistrations => _lifecycle.CanRegisterSensors;

        public string ComputerName => _options?.ComputerName;

        public string Module => _options?.Module;


        public event Action ToStarting;
        public event Action ToRunning;
        public event Action ToStopping;
        public event Action ToStopped;


        [Obsolete]
        public event EventHandler ValuesQueueOverflow;


        public IEnumerable<ISensor> DefaultSensors => _sensorsStorage.Values;


        /// <summary>
        /// Creates new instance of <see cref="DataCollector"/> class, initializing main parameters
        /// </summary>
        /// <param name="options">Common options for datacollector</param>
        public DataCollector(CollectorOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _options.Validate();

            options.DataSender = options.DataSender ?? new HsmHttpsClient(options, _logger);

            _dataSender = options.DataSender;

            _scheduler = new CollectorScheduler();

            _dataProcessor = new DataProcessor(options, _lifecycle, _opLock, _scheduler, _logger);

            _sensorsStorage = _dataProcessor.SensorStorage;

            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;
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


        public Task<ConnectionResult> TestConnection() => _dataSender.TestConnectionAsync().AsTask();

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

        public IDataCollector AddLifecycleListener(ILifecycleListener listener)
        {
            if (listener == null)
                return this;

            lock (_listenersLock)
                _lifecycleListeners.Add(listener);

            return this;
        }

        [Obsolete("Use method AddNLog() to add logging and method Start() after default sensors initialization")]
        public void Initialize(bool useLogging = true, string folderPath = null, string fileNameFormat = null)
        {
            if (useLogging)
                AddNLog();

            InitializeProcessor();
        }

        [Obsolete("Use Initialize(bool, string, string)")]
        public void Initialize()
        {
            InitializeProcessor();
        }


        public Task Start() => Start(Task.CompletedTask);

        public async Task Start(Task customStartingTask)
        {
            bool processorStarted;

            // Take _opLock through the physical processor start. Otherwise a concurrent Stop could
            // run StopAsync against queues that have not been started yet, then this method would
            // bring them up afterwards — leaving live background tasks while public Status == Stopped.
            lock (_opLock)
            {
                if (!_lifecycle.TryStart())
                    return;

                LogAndRaise(CollectorStatus.Starting);

                if (!Status.IsStartingOrRunning())
                    return;

                try
                {
                    processorStarted = _dataProcessor.Start();
                }
                catch (Exception ex)
                {
                    _logger.Error($"DataCollector starting error during processor start: {ex}");
                    processorStarted = false;
                }

                if (!processorStarted)
                {
                    if (_lifecycle.AbortStart())
                        LogAndRaise(CollectorStatus.Stopped);
                    return;
                }
            }

            // Queues are now running. Any exit path from here must roll them back if the lifecycle
            // was cancelled by a concurrent Stop/Dispose, or background processors will outlive Status.
            try
            {
                await customStartingTask.ConfigureAwait(false);

                Task initTask;
                lock (_opLock)
                {
                    if (!Status.IsStartingOrRunning())
                        return;

                    initTask = _dataProcessor.InitAsync();
                    _currentStartInitTask = initTask;
                }

                try
                {
                    await initTask.ConfigureAwait(false);
                }
                finally
                {
                    lock (_opLock)
                    {
                        if (ReferenceEquals(_currentStartInitTask, initTask))
                            _currentStartInitTask = null;
                    }
                }

                lock (_opLock)
                {
                    if (_lifecycle.CompleteStart())
                        LogAndRaise(CollectorStatus.Running);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"DataCollector starting error: {ex}");

                await SafeStopProcessor().ConfigureAwait(false);

                lock (_opLock)
                {
                    if (_lifecycle.AbortStart())
                        LogAndRaise(CollectorStatus.Stopped);
                }
            }
        }

        private async Task WaitForStartInitThenStopProcessor(Task startInitTask)
        {
            if (startInitTask != null)
            {
                try
                {
                    await startInitTask.ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.Error($"DataCollector waiting for in-flight start initialization failed: {ex}");
                }
            }

            await _dataProcessor.StopAsync().ConfigureAwait(false);
        }

        private async Task SafeStopProcessor()
        {
            try
            {
                await _dataProcessor.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"DataCollector start-rollback stop error: {ex}");
            }
        }


        public Task Stop() => Stop(Task.CompletedTask);

        public async Task Stop(Task customStoppingTask)
        {
            Task startInitTask;
            Task processorStopTask;
            Task stopTask;

            lock (_opLock)
            {
                if (!_lifecycle.TryStop())
                    return;

                LogAndRaise(CollectorStatus.Stopping);

                startInitTask = _currentStartInitTask;
                processorStopTask = WaitForStartInitThenStopProcessor(startInitTask);
                stopTask = Task.WhenAll(processorStopTask, customStoppingTask);
                _currentProcessorStopTask = processorStopTask;
            }

            try
            {
                await stopTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Error($"DataCollector Stop error: {ex}");
            }

            lock (_opLock)
            {
                // Clear only if still pointing to our task — Dispose may have overwritten with its own.
                if (ReferenceEquals(_currentProcessorStopTask, processorStopTask))
                    _currentProcessorStopTask = null;

                if (_lifecycle.CompleteStop())
                    LogAndRaise(CollectorStatus.Stopped);
            }
        }

        private void InitializeProcessor()
        {
            bool processorStarted;

            lock (_opLock)
            {
                if (!_lifecycle.TryStart())
                    return;

                LogAndRaise(CollectorStatus.Starting);

                _logger.Info("Initialize timer...");

                try
                {
                    processorStarted = _dataProcessor.Start();
                }
                catch (Exception ex)
                {
                    _logger.Error($"DataCollector initialization error during processor start: {ex}");
                    processorStarted = false;
                }

                if (!processorStarted)
                {
                    if (_lifecycle.AbortStart())
                        LogAndRaise(CollectorStatus.Stopped);
                    return;
                }
            }

            try
            {
                _dataProcessor.InitAsync().ConfigureAwait(false).GetAwaiter().GetResult();

                bool completed;
                lock (_opLock)
                {
                    completed = _lifecycle.CompleteStart();
                    if (completed)
                        LogAndRaise(CollectorStatus.Running);
                }

                if (!completed)
                    SafeStopProcessor().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.Error($"DataCollector initialization error: {ex}");

                SafeStopProcessor().ConfigureAwait(false).GetAwaiter().GetResult();

                lock (_opLock)
                {
                    if (_lifecycle.AbortStart())
                        LogAndRaise(CollectorStatus.Stopped);
                }
            }
        }


        public void Dispose()
        {
            CollectorStatus previousStatus;
            Task inFlightStop;
            Task inFlightStartInit;
            bool ownsStop;

            lock (_opLock)
            {
                previousStatus = _lifecycle.TryDispose();

                if (previousStatus == CollectorStatus.Disposed)
                    return;

                // If another thread is already stopping, do not issue a duplicate StopAsync —
                // wait for its task. Otherwise take the Starting/Running -> Stopping transition ourselves.
                inFlightStop = _currentProcessorStopTask;
                inFlightStartInit = _currentStartInitTask;
                ownsStop = inFlightStop == null
                    && previousStatus.IsStartingOrRunning()
                    && _lifecycle.TryStop();

                if (ownsStop)
                    LogAndRaise(CollectorStatus.Stopping);
            }

            try
            {
                if (inFlightStop != null)
                {
                    // Concurrent Stop() is responsible for firing ToStopped — but its continuation runs on
                    // the threadpool after the inner stop task completes, with no ordering guarantee relative
                    // to GetAwaiter().GetResult() returning here. Without coordination, Stop's continuation
                    // could fire ToStopped after Dispose has already torn down components.
                    try
                    {
                        inFlightStop.ConfigureAwait(false).GetAwaiter().GetResult();
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"DataCollector waiting for in-flight stop failed: {ex}");
                    }

                    // Race the continuation: whichever path acquires _opLock first calls CompleteStop;
                    // the other finds the transition already done and no-ops. Either way ToStopped fires
                    // exactly once, and always before component disposal below.
                    lock (_opLock)
                    {
                        if (_lifecycle.CompleteStop())
                            LogAndRaise(CollectorStatus.Stopped);
                    }
                }
                else if (ownsStop)
                {
                    DisposeComponent(() => WaitForStartInitThenStopProcessor(inFlightStartInit).ConfigureAwait(false).GetAwaiter().GetResult(), nameof(_dataProcessor));

                    lock (_opLock)
                    {
                        if (_lifecycle.CompleteStop())
                            LogAndRaise(CollectorStatus.Stopped);
                    }
                }

                DisposeComponent(_dataProcessor.Dispose, nameof(_dataProcessor));

                DisposeComponent(_dataSender.Dispose, nameof(_dataSender));

                DisposeComponent(_scheduler.Dispose, nameof(_scheduler));
            }
            finally
            {
                AppDomain.CurrentDomain.UnhandledException -= UnhandledExceptionHandler;
            }
        }


        private void LogAndRaise(CollectorStatus status)
        {
            _logger.Info($"DataCollector (v. {DataCollectorExtensions.Version}) -> {status}");

            switch (status)
            {
                case CollectorStatus.Starting:
                    RaiseLifecycleEvent(ToStarting, nameof(ToStarting));
                    break;
                case CollectorStatus.Running:
                    RaiseLifecycleEvent(ToRunning, nameof(ToRunning));
                    break;
                case CollectorStatus.Stopping:
                    RaiseLifecycleEvent(ToStopping, nameof(ToStopping));
                    break;
                case CollectorStatus.Stopped:
                    RaiseLifecycleEvent(ToStopped, nameof(ToStopped));
                    break;
            }

            NotifyLifecycleListeners(status);
        }

        private void NotifyLifecycleListeners(CollectorStatus status)
        {
            ILifecycleListener[] snapshot;
            lock (_listenersLock)
            {
                if (_lifecycleListeners.Count == 0)
                    return;

                snapshot = _lifecycleListeners.ToArray();
            }

            foreach (var listener in snapshot)
            {
                try
                {
                    switch (status)
                    {
                        case CollectorStatus.Starting:
                            listener.OnStarting();
                            break;
                        case CollectorStatus.Running:
                            listener.OnRunning();
                            break;
                        case CollectorStatus.Stopping:
                            listener.OnStopping();
                            break;
                        case CollectorStatus.Stopped:
                            listener.OnStopped();
                            break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"DataCollector lifecycle listener error on {status}: {ex}");
                }
            }
        }

        private void RaiseLifecycleEvent(Action lifecycleEvent, string eventName)
        {
            if (lifecycleEvent == null)
                return;

            foreach (Action handler in lifecycleEvent.GetInvocationList())
            {
                try
                {
                    handler();
                }
                catch (Exception ex)
                {
                    _logger.Error($"DataCollector {eventName} event handler error: {ex}");
                }
            }
        }

        private void DisposeComponent(Action dispose, string componentName)
        {
            try
            {
                dispose();
            }
            catch (Exception ex)
            {
                _logger.Error($"DataCollector {componentName} dispose error: {ex}");
            }
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
                {
                    Windows.AddProcessThreadCount(options);
                    Windows.AddProcessThreadPoolThreadCount(options);
                }
            }
            else
            {
                if (isCPU)
                    Unix.AddProcessCpu(options);
                if (isMemory)
                    Unix.AddProcessMemory(options);
                if (isThreads)
                {
                    Unix.AddProcessThreadCount(options);
                    Unix.AddProcessThreadPoolThreadCount(options);
                }
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

        public IInstantValueSensor<Version> CreateVersionSensor(string path, string description = "") =>CreateInstantSensor<Version>(path, description);

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

        public IInstantValueSensor<int> CreateEnumSensor(string path, string description = "") =>
            CreateEnumSensor(path, new EnumSensorOptions { Description = description });

        public IInstantValueSensor<int> CreateEnumSensor(string path, EnumSensorOptions options) => _sensorsStorage.CreateEnumInstantSensor(path, options);


        private IInstantValueSensor<T> CreateInstantSensor<T>(string path, string description) =>
            CreateInstantSensor<T>(path, new InstantSensorOptions()
            {
                Description = description,
            });

        private IInstantValueSensor<T> CreateInstantSensor<T>(string path, InstantSensorOptions options) => _sensorsStorage.CreateInstantSensor<T>(path, options);


        public IServiceCommandsSensor CreateServiceCommandsSensor() => _sensorsStorage.CreateServiceCommandsSensor();


        public IMonitoringRateSensor CreateM1RateSensor(string path, string desctiption = "") => CreateRateSensor(path, TimeSpan.FromMinutes(1), desctiption);

        public IMonitoringRateSensor CreateM5RateSensor(string path, string description = "") => CreateRateSensor(path, TimeSpan.FromMinutes(5), description);

        public IMonitoringRateSensor CreateRateSensor(string path, RateSensorOptions options) => _sensorsStorage.CreateRateSensor(path, options);

        private IMonitoringRateSensor CreateRateSensor(string path, TimeSpan postPeriod, string description = "") => CreateRateSensor(path, new RateSensorOptions
        {
            PostDataPeriod = postPeriod,
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

        public IBarSensor<int> Create1HrIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, TimeSpan.FromHours(1), TimeSpan.FromSeconds(15), description);

        public IBarSensor<int> Create30MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(15), description);

        public IBarSensor<int> Create10MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(15), description);

        public IBarSensor<int> Create5MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(15), description);

        public IBarSensor<int> Create1MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15), description);

        public IBarSensor<int> CreateIntBarSensor(string path, TimeSpan barPeriod, TimeSpan postPeriod, string description = "") =>
            CreateIntBarSensor(path, BuildBarOptions(barPeriod, postPeriod, description));

        public IBarSensor<int> CreateIntBarSensor(string path, int barPeriod = 300000, int postPeriod = 15000, string description = "") =>
             CreateIntBarSensor(path, BuildBarOptions(TimeSpan.FromMilliseconds(barPeriod), TimeSpan.FromMilliseconds(postPeriod), description));

        public IBarSensor<int> CreateIntBarSensor(string path, BarSensorOptions options) => _sensorsStorage.CreateIntBarSensor(path, options);


        public IBarSensor<double> Create1HrDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, TimeSpan.FromHours(1), TimeSpan.FromSeconds(15), precision, description);

        public IBarSensor<double> Create30MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, TimeSpan.FromMinutes(30), TimeSpan.FromSeconds(15), precision, description);

        public IBarSensor<double> Create10MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(15), precision, description);

        public IBarSensor<double> Create5MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(15), precision, description);

        public IBarSensor<double> Create1MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(15), precision, description);

        public IBarSensor<double> CreateDoubleBarSensor(string path, TimeSpan barPeriod, TimeSpan postPeriod, int precision = 2, string description = "") =>
            CreateDoubleBarSensor(path, BuildBarOptions(barPeriod, postPeriod, description, precision));

        public IBarSensor<double> CreateDoubleBarSensor(string path, int barPeriod = 300000, int postPeriod = 15000, int precision = 2, string description = "") =>
            CreateDoubleBarSensor(path, BuildBarOptions(TimeSpan.FromMilliseconds(barPeriod), TimeSpan.FromMilliseconds(postPeriod), description, precision));

        public IBarSensor<double> CreateDoubleBarSensor(string path, BarSensorOptions options) => _sensorsStorage.CreateDoubleBarSensor(path, options);


        private static BarSensorOptions BuildBarOptions(TimeSpan barPeriod, TimeSpan postPeriod, string description, int precision = 2) =>
            new BarSensorOptions()
            {
                PostDataPeriod = postPeriod,
                BarPeriod = barPeriod,

                Description = description,
                Precision = precision,
            };

        #endregion

        #region Generic func sensors

        public INoParamsFuncSensor<T> CreateNoParamsFuncSensor<T>(string path, string description, Func<T> function, int millisecondsInterval) =>
            CreateNoParamsFuncSensor(path, description, function, TimeSpan.FromMilliseconds(millisecondsInterval));

        public INoParamsFuncSensor<T> Create1MinNoParamsFuncSensor<T>(string path, string description, Func<T> function) =>
            CreateNoParamsFuncSensor(path, description, function, TimeSpan.FromMinutes(1));

        public INoParamsFuncSensor<T> Create5MinNoParamsFuncSensor<T>(string path, string description, Func<T> function) =>
            CreateNoParamsFuncSensor(path, description, function, TimeSpan.FromMinutes(5));

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
            CreateParamsFuncSensor(path, description, function, TimeSpan.FromMinutes(1));

        public IParamsFuncSensor<T, U> Create5MinParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function) =>
            CreateParamsFuncSensor(path, description, function, TimeSpan.FromMinutes(5));

        public IParamsFuncSensor<T, U> CreateValuesFunctionSensor<T, U>(string path, Func<List<U>, T> function, ValuesFunctionSensorOptions options) =>
            _sensorsStorage.CreateValuesFunctionSensor(path, function, options);

        public IParamsFuncSensor<T, U> CreateParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function, TimeSpan interval) =>
            CreateValuesFunctionSensor(path, function, new ValuesFunctionSensorOptions()
            {
                PostDataPeriod = interval,
                Description = description,
            });

        #endregion

        private void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception exception)
                _logger.Error($"An unhandled exception occurred [Runtime terminated = {e.IsTerminating}]: {exception}");
        }

    }
}
