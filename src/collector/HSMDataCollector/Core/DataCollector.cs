using HSMDataCollector.Bar;
using HSMDataCollector.Base;
using HSMDataCollector.CustomFuncSensor;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultValueSensor;
using HSMDataCollector.Exceptions;
using HSMDataCollector.InstantValue;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SensorBase = HSMDataCollector.Base.SensorBase;

namespace HSMDataCollector.Core
{
    /// <summary>
    /// Main monitoring class which is used to create and control sensors' instances
    /// </summary>
    public sealed class DataCollector : IDataCollector
    {
        private readonly LoggerManager _logManager = new LoggerManager();

        private readonly ConcurrentDictionary<string, ISensor> _nameToSensor = new ConcurrentDictionary<string, ISensor>();
        private readonly DefaultSensorsCollection _defaultSensors;
        private readonly SensorsDefaultOptions _sensorsOptions;
        private readonly SensorsStorage _sensorsStorage;
        private readonly IDataQueue _dataQueue;
        private readonly HSMClient _hsmClient;

        private bool _isStopped;

        public IWindowsCollection Windows => _defaultSensors;

        public IUnixCollection Unix => _defaultSensors;


        [Obsolete]
        public event EventHandler ValuesQueueOverflow;

        /// <summary>
        /// Creates new instance of <see cref="DataCollector"/> class, initializing main parameters
        /// </summary>
        /// <param name="options">Common options for datacollector</param>
        public DataCollector(CollectorOptions options)
        {
            _dataQueue = new DataQueue(options);
            _sensorsStorage = new SensorsStorage(_dataQueue as IValuesQueue, _logManager);
            _hsmClient = new HSMClient(options, _dataQueue, _logManager);
            
            _sensorsOptions = new SensorsDefaultOptions();
            _defaultSensors = new DefaultSensorsCollection(_sensorsStorage, _sensorsOptions);
        }

        /// <summary>
        /// Creates new instance of <see cref="DataCollector"/> class, initializing main parameters
        /// </summary>
        /// <param name="productKey">Key, which identifies the product (logical group) for all sensors that will be created.</param>
        /// <param name="address">HSM server address to send data to (Do not forget https:// if needed)</param>
        /// <param name="port">HSM sensors API port, which defaults to 44330. Specify if your HSM server Docker container configured differently.</param>
        [Obsolete("Use constructor with DataCollectorOptions")]
        public DataCollector(string productKey, string address, int port = 44330)
            : this(new CollectorOptions() { AccessKey = productKey, ServerAddress = address, Port = port })
        { }


        public void Dispose()
        {
            Stop();
        }

        public IDataCollector AddNLog(LoggerOptions options = null)
        {
            _logManager.InitializeLogger(options);

            return this;
        }

        [Obsolete("Use method AddNLog() to add logging and method Start() after default sensors initialization")]
        public void Initialize(bool useLogging = true, string folderPath = null, string fileNameFormat = null)
        {
            if (useLogging)
                AddNLog();

            _logManager.Logger?.Info("Initialize timer...");
            _dataQueue.InitializeTimer();
        }

        [Obsolete("Use Initialize(bool, string, string)")]
        public void Initialize()
        {
            _dataQueue.InitializeTimer();
        }

        public Task Start()
        {
            _dataQueue.InitializeTimer();
            
            return _sensorsStorage.Start();
        }

        public void Stop()
        {
            if (_isStopped)
                return;

            _logManager.Logger?.Info("DataCollector stopping...");

            _sensorsStorage.Dispose();

            var allData = new List<SensorValueBase>(1 << 3);
            if (_dataQueue != null)
            {
                allData.AddRange(_dataQueue.DequeueData());
                _dataQueue.Stop();
            }

            Thread.Sleep(TimeSpan.FromSeconds(1));

            foreach (var pair in _nameToSensor)
                if (pair.Value.HasLastValue)
                    allData.Add(pair.Value.GetLastValue());

            foreach (var pair in _nameToSensor)
                pair.Value.Dispose();

            if (allData.Count != 0)
                _hsmClient.SendMonitoringData(allData);

            _hsmClient?.Dispose();
            _isStopped = true;
            _logManager.Logger?.Info("DataCollector successfully stopped.");
        }

        [Obsolete("Use method AddSystemMonitoringSensors(options) in Windows collection")]
        public void InitializeSystemMonitoring(bool isCPU, bool isFreeRam, string specificPath = null)
        {
            if (!_defaultSensors.IsUnixOS)
            {
                var options = _sensorsOptions.SystemMonitoring.GetAndFill(new BarSensorOptions() { NodePath = specificPath });

                if (isCPU)
                    Windows.AddTotalCpu(options);
                if (isFreeRam)
                    Windows.AddFreeRamMemory(options);
            }

            Start();
        }

        [Obsolete("Use method AddProcessSensors(options) in Windows or Unix collections")]
        public void InitializeProcessMonitoring(bool isCPU, bool isMemory, bool isThreads, string specificPath = null)
        {
            var options = _sensorsOptions.ProcessMonitoring.GetAndFill(new BarSensorOptions() { NodePath = specificPath });

            if (_defaultSensors.IsUnixOS)
            {
                if (isCPU)
                    Unix.AddProcessCpu(options);
                if (isMemory)
                    Unix.AddProcessMemory(options);
                if (isThreads)
                    Unix.AddProcessThreadCount(options);
            }
            else
            {
                if (isCPU)
                    Windows.AddProcessCpu(options);
                if (isMemory)
                    Windows.AddProcessMemory(options);
                if (isThreads)
                    Windows.AddProcessThreadCount(options);
            }

            Start();
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
            var options = _sensorsOptions.CollectorAliveMonitoring.GetAndFill(new MonitoringSensorOptions() { NodePath = specificPath });

            if (_defaultSensors.IsUnixOS)
                Unix.AddCollectorHeartbeat(options);
            else
                Windows.AddCollectorHeartbeat(options);

            Start();
        }

        [Obsolete("Use method AddWindowsSensors(options) in Windows collection")]
        public bool InitializeWindowsUpdateMonitoring(TimeSpan sensorInterval, TimeSpan updateInterval, string specificPath = null)
        {
            try
            {
                var options = new WindowsSensorOptions()
                {
                    NodePath = specificPath,
                    PostDataPeriod = sensorInterval,
                    AcceptableUpdateInterval = updateInterval,
                };

                Windows.AddWindowsNeedUpdate(_sensorsOptions.WindowsInfoMonitoring.GetAndFill(options));
            }
            catch
            {
                return false;
            }

            Start();

            return true;
        }

        public bool IsSensorExists(string path) => _nameToSensor.ContainsKey(path) || _sensorsStorage.ContainsKey(path);

        #region Generic sensors functionality

        public IInstantValueSensor<bool> CreateBoolSensor(string path, string description)
        {
            return CreateInstantValueSensorInternal<bool>(path, description);
        }

        public IInstantValueSensor<int> CreateIntSensor(string path, string description)
        {
            return CreateInstantValueSensorInternal<int>(path, description);
        }

        public IInstantValueSensor<double> CreateDoubleSensor(string path, string description)
        {
            return CreateInstantValueSensorInternal<double>(path, description);
        }

        public IInstantValueSensor<string> CreateStringSensor(string path, string description)
        {
            return CreateInstantValueSensorInternal<string>(path, description);
        }

        public IInstantValueSensor<string> CreateFileSensor(string path, string fileName, string extension = "txt", string description = "")
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is IInstantValueSensor<string> instantValueSensor)
                return instantValueSensor;

            var sensor = new InstantFileSensor(path, fileName, extension, _dataQueue as IValuesQueue, description);
            AddNewSensor(sensor, path);
            
            return sensor;
        }

        public Task SendFileAsync(string sensorPath, string filePath, SensorStatus status = SensorStatus.Ok, string comment = "")
        {
            if (!File.Exists(filePath))
                return default;
            
            var file = new FileInfo(filePath);
            
            return _hsmClient.SendFileAsync(file, sensorPath, status, comment);
        }   

        public ILastValueSensor<bool> CreateLastValueBoolSensor(string path, bool defaultValue, string description = "")
        {
            return CreateLastValueSensorInternal(path, defaultValue, description);
        }

        public ILastValueSensor<int> CreateLastValueIntSensor(string path, int defaultValue, string description = "")
        {
            return CreateLastValueSensorInternal(path, defaultValue, description);
        }

        public ILastValueSensor<double> CreateLastValueDoubleSensor(string path, double defaultValue, string description = "")
        {
            return CreateLastValueSensorInternal(path, defaultValue, description);
        }

        public ILastValueSensor<string> CreateLastValueStringSensor(string path, string defaultValue, string description = "")
        {
            return CreateLastValueSensorInternal(path, defaultValue, description);
        }

        private IInstantValueSensor<T> CreateInstantValueSensorInternal<T>(string path, string description)
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is IInstantValueSensor<T> instantValueSensor)
                return instantValueSensor;

            InstantValueSensor<T> sensor = new InstantValueSensor<T>(path, _dataQueue as IValuesQueue, description);
            AddNewSensor(sensor, path);

            return sensor;
        }

        private ILastValueSensor<T> CreateLastValueSensorInternal<T>(string path, T defaultValue, string description = "")
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is ILastValueSensor<T> lastValueSensor)
                return lastValueSensor;

            DefaultValueSensor<T> sensor =
                new DefaultValueSensor<T>(path, _dataQueue as IValuesQueue, defaultValue, description);
            AddNewSensor(sensor, path);

            return sensor;
        }

        #endregion

        #region Generic bar sensors

        public IBarSensor<int> CreateIntBarSensor(string path, int timeout, int smallPeriod = 15000, string description = "")
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is IBarSensor<int> intBarSensor)
            {
                (intBarSensor as BarSensorBase)?.Restart(timeout, smallPeriod);

                return intBarSensor;
            }

            BarSensor<int> sensor = new BarSensor<int>(path, _dataQueue as IValuesQueue, SensorType.IntegerBarSensor,
                timeout, smallPeriod, description);
            AddNewSensor(sensor, path);

            return sensor;
        }

        public IBarSensor<int> Create1HrIntBarSensor(string path, string description)
        {
            return CreateIntBarSensor(path, 3600000, 15000, description);
        }

        public IBarSensor<int> Create30MinIntBarSensor(string path, string description)
        {
            return CreateIntBarSensor(path, 1800000, 15000, description);
        }

        public IBarSensor<int> Create10MinIntBarSensor(string path, string description)
        {
            return CreateIntBarSensor(path, 600000, 15000, description);
        }

        public IBarSensor<int> Create5MinIntBarSensor(string path, string description)
        {
            return CreateIntBarSensor(path, 300000, 15000, description);
        }

        public IBarSensor<int> Create1MinIntBarSensor(string path, string description)
        {
            return CreateIntBarSensor(path, 60000, 15000, description);
        }

        public IBarSensor<double> CreateDoubleBarSensor(string path, int timeout, int smallPeriod, int precision, string description = "")
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is IBarSensor<double> doubleBarSensor)
            {
                (doubleBarSensor as BarSensorBase)?.Restart(timeout, smallPeriod);

                return doubleBarSensor;
            }

            BarSensor<double> sensor = new BarSensor<double>(path, _dataQueue as IValuesQueue,
                SensorType.DoubleBarSensor, timeout, smallPeriod, precision, description);
            AddNewSensor(sensor, path);

            return sensor;
        }

        public IBarSensor<double> Create1HrDoubleBarSensor(string path, int precision, string description)
        {
            return CreateDoubleBarSensor(path, 3600000, 15000, precision, description);
        }

        public IBarSensor<double> Create30MinDoubleBarSensor(string path, int precision, string description)
        {
            return CreateDoubleBarSensor(path, 1800000, 15000, precision, description);
        }

        public IBarSensor<double> Create10MinDoubleBarSensor(string path, int precision, string description)
        {
            return CreateDoubleBarSensor(path, 600000, 15000, precision, description);
        }

        public IBarSensor<double> Create5MinDoubleBarSensor(string path, int precision, string description)
        {
            return CreateDoubleBarSensor(path, 300000, 15000, precision, description);
        }

        public IBarSensor<double> Create1MinDoubleBarSensor(string path, int precision, string description)
        {
            return CreateDoubleBarSensor(path, 60000, 15000, precision, description);
        }

        #endregion

        #region Generic func sensors

        public INoParamsFuncSensor<T> CreateNoParamsFuncSensor<T>(string path, string description, Func<T> function, TimeSpan interval)
        {
            return CreateNoParamsFuncSensorInternal(path, description, function, interval);
        }

        public INoParamsFuncSensor<T> CreateNoParamsFuncSensor<T>(string path, string description, Func<T> function, int millisecondsInterval)
        {
            return CreateNoParamsFuncSensorInternal(path, description, function, TimeSpan.FromMilliseconds(millisecondsInterval));
        }

        public INoParamsFuncSensor<T> Create1MinNoParamsFuncSensor<T>(string path, string description, Func<T> function)
        {
            return CreateNoParamsFuncSensorInternal(path, description, function, TimeSpan.FromMilliseconds(60000));
        }

        public INoParamsFuncSensor<T> Create5MinNoParamsFuncSensor<T>(string path, string description, Func<T> function)
        {
            return CreateNoParamsFuncSensorInternal(path, description, function, TimeSpan.FromMilliseconds(300000));
        }

        public IParamsFuncSensor<T, U> CreateParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function, TimeSpan interval)
        {
            return CreateParamsFuncSensorInternal(path, description, function, interval);
        }

        public IParamsFuncSensor<T, U> CreateParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function, int millisecondsInterval)
        {
            return CreateParamsFuncSensorInternal(path, description, function, TimeSpan.FromMilliseconds(millisecondsInterval));
        }

        public IParamsFuncSensor<T, U> Create1MinParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function)
        {
            return CreateParamsFuncSensorInternal(path, description, function, TimeSpan.FromMilliseconds(60000));
        }

        public IParamsFuncSensor<T, U> Create5MinParamsFuncSensor<T, U>(string path, string description, Func<List<U>, T> function)
        {
            return CreateParamsFuncSensorInternal(path, description, function, TimeSpan.FromMilliseconds(300000));
        }

        private IParamsFuncSensor<T, U> CreateParamsFuncSensorInternal<T, U>(string path, string description,
            Func<List<U>, T> function, TimeSpan interval)
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is IParamsFuncSensor<T, U> typedSensor)
                return typedSensor;

            OneParamFuncSensor<T, U> sensor = new OneParamFuncSensor<T, U>(path, _dataQueue as IValuesQueue, description, interval, function, _logManager.Logger);
            AddNewSensor(sensor, path);

            return sensor;
        }

        private INoParamsFuncSensor<T> CreateNoParamsFuncSensorInternal<T>(string path, string description, Func<T> function,
            TimeSpan interval)
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is INoParamsFuncSensor<T> typedSensor)
                return typedSensor;

            NoParamsFuncSensor<T> sensor = new NoParamsFuncSensor<T>(path, _dataQueue as IValuesQueue, description, interval, function, _logManager.Logger);
            AddNewSensor(sensor, path);

            return sensor;
        }

        #endregion

        private SensorBase GetExistingSensor(string path)
        {
            if (_sensorsStorage.ContainsKey(path))
            {
                var message = $"Path {path} is used by standard performance sensor!";
                _logManager.Logger?.Error(message);

                throw new InvalidSensorPathException(message);
            }

            if (_nameToSensor.TryGetValue(path, out var readValue))
                return readValue as SensorBase;

            return null;
        }

        private void AddNewSensor(ISensor sensor, string path)
        {
            _nameToSensor[path] = sensor;

            _logManager.Logger?.Info($"Added new sensor {path}");
        }
    }
}
