﻿using HSMDataCollector.Base;
using HSMDataCollector.CustomFuncSensor;
using HSMDataCollector.DefaultSensors;
using HSMDataCollector.DefaultValueSensor;
using HSMDataCollector.Exceptions;
using HSMDataCollector.Extensions;
using HSMDataCollector.InstantValue;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SensorBase = HSMDataCollector.Base.SensorBase;

namespace HSMDataCollector.Core
{
    public enum CollectorStatus : byte
    {
        Starting = 0,
        Running,
        Stopping,
        Stopped,
    }

    /// <summary>
    /// Main monitoring class which is used to create and control sensors' instances
    /// </summary>
    public sealed class DataCollector : IDataCollector
    {
        private readonly LoggerManager _logger = new LoggerManager();

        private readonly ConcurrentDictionary<string, ISensor> _nameToSensor = new ConcurrentDictionary<string, ISensor>();
        private readonly SensorsPrototype _sensorsPrototype = new SensorsPrototype();
        private readonly SensorsStorage _sensorsStorage;
        private readonly IDataQueue _dataQueue;
        private readonly HSMClient _hsmClient;


        internal static bool IsWindowsOS { get; } = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

        private DefaultSensorsCollection CurrentCollection => IsWindowsOS ? (DefaultSensorsCollection)Windows : (DefaultSensorsCollection)Unix;


        public IWindowsCollection Windows { get; }

        public IUnixCollection Unix { get; }


        public CollectorStatus Status { get; private set; } = CollectorStatus.Stopped;


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
            _dataQueue = new DataQueue(options);
            _sensorsStorage = new SensorsStorage(_dataQueue as IValuesQueue, _logger);

            Windows = new WindowsSensorsCollection(_sensorsStorage, _sensorsPrototype);
            Unix = new UnixSensorsCollection(_sensorsStorage, _sensorsPrototype);

            _hsmClient = new HSMClient(options, _dataQueue, _logger);

            ToRunning += ToStartingCollector;
            ToStopped += ToStoppedCollector;
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
            _dataQueue.Init();
        }

        [Obsolete("Use Initialize(bool, string, string)")]
        public void Initialize()
        {
            _dataQueue.Init();
        }


        public Task Start() => Start(Task.CompletedTask);

        public async Task Start(Task customStartingTask)
        {
            try
            {
                if (!Status.IsStopped())
                    return;

                ChangeStatus(CollectorStatus.Starting);

                foreach (var oldSensor in _nameToSensor.Values)
                    oldSensor.Start();

                await Task.WhenAll(_sensorsStorage.Init(), customStartingTask);

                ChangeStatus(CollectorStatus.Running);
            }
            catch (Exception ex)
            {
                _logger.Error(ex);

                StopSensors(ex.Message);
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

                StopSensors();
            }
            catch (Exception ex)
            {
                _logger.Error(ex);

                StopSensors(ex.Message);
            }
        }

        public void Dispose()
        {
            if (!Status.IsRunning())
                return;

            ChangeStatus(CollectorStatus.Stopping);

            _sensorsStorage.Dispose();

            StopSensors();

            ToRunning -= ToStartingCollector;
            ToStopped -= ToStoppedCollector;

            _hsmClient.Dispose();
        }


        public bool IsSensorExists(string path) => _nameToSensor.ContainsKey(path) || _sensorsStorage.ContainsKey(path);


        private void StopSensors(string error = null)
        {
            var lastData = _nameToSensor.Values.Where(v => v.HasLastValue).Select(v => v.GetLastValue()).ToList();

            if (lastData.Count > 0)
                _hsmClient.SendData(lastData);

            foreach (var pair in _nameToSensor.Values)
                pair.Dispose();

            ChangeStatus(CollectorStatus.Stopped, error);
        }

        private void ChangeStatus(CollectorStatus newStatus, string error = null)
        {
            Status = newStatus;

            _logger.Info($"DataCollector (v. {DataCollectorExtensions.Version}) -> {newStatus}");

            CurrentCollection.StatusSensor?.BuildAndSendValue(_hsmClient, newStatus, error);

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

        private void ToStartingCollector()
        {
            _dataQueue.Init();

            CurrentCollection.ProductVersion?.StartInfo();
            CurrentCollection.CollectorVersion?.StartInfo();

            _ = _sensorsStorage.Start();
        }

        private void ToStoppedCollector()
        {
            CurrentCollection.ProductVersion?.StopInfo();
            CurrentCollection.CollectorVersion?.StopInfo();

            _dataQueue.Stop();
        }

        #region Obsolets

        [Obsolete("Use method AddSystemMonitoringSensors(options) in Windows collection")]
        public void InitializeSystemMonitoring(bool isCPU, bool isFreeRam, string specificPath = null)
        {
            if (IsWindowsOS)
            {
                var options = _sensorsPrototype.SystemMonitoring.GetAndFill(new BarSensorOptions() { NodePath = specificPath });

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
            var options = _sensorsPrototype.ProcessMonitoring.GetAndFill(new BarSensorOptions() { NodePath = specificPath });

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
            var options = _sensorsPrototype.CollectorAlive.GetAndFill(new CollectorMonitoringInfoOptions() { NodePath = specificPath });

            if (IsWindowsOS)
                Windows.AddCollectorAlive(options);
            else
                Unix.AddCollectorAlive(options);

            _ = Start();
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

                Windows.AddWindowsNeedUpdate(_sensorsPrototype.WindowsInfo.GetAndFill(options));
            }
            catch
            {
                return false;
            }

            _ = Start();

            return true;
        }

        #endregion

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
            {
                _logger.Error($"{filePath} does not exist");
                return default;
            }

            _logger.Info($"Sending {filePath} to {sensorPath}");

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

            var sensor = new InstantValueSensor<T>(path, _dataQueue as IValuesQueue, description);
            AddNewSensor(sensor, path);

            return sensor;
        }

        private ILastValueSensor<T> CreateLastValueSensorInternal<T>(string path, T defaultValue, string description = "")
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is ILastValueSensor<T> lastValueSensor)
                return lastValueSensor;

            var sensor = new DefaultValueSensor<T>(path, _dataQueue as IValuesQueue, defaultValue, description);
            AddNewSensor(sensor, path);

            return sensor;
        }

        #endregion

        #region Generic bar sensors

        public IBarSensor<int> CreateIntBarSensor(string path, int barPeriod, int postPeriod = 15000, string description = "")
        {
            var split = path.Split('/');
            var name = split.LastOrDefault();

            var nodePathIndex = path.Length - name.Length - 1;
            var nodePath = nodePathIndex > -1 ? path.Substring(0, nodePathIndex) : string.Empty;

            var options = new BarSensorOptions()
            {
                SensorName = name,
                NodePath = nodePath,
                CollectBarPeriod = TimeSpan.FromMilliseconds(barPeriod),
                PostDataPeriod = TimeSpan.FromMilliseconds(postPeriod),
            };

            return CreateBarSensor(new IntBarPublicSensor(options));
        }

        public IBarSensor<int> Create1HrIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 3600000, 15000, description);

        public IBarSensor<int> Create30MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 1800000, 15000, description);

        public IBarSensor<int> Create10MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 600000, 15000, description);

        public IBarSensor<int> Create5MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 300000, 15000, description);

        public IBarSensor<int> Create1MinIntBarSensor(string path, string description = "") => CreateIntBarSensor(path, 60000, 15000, description);


        public IBarSensor<double> CreateDoubleBarSensor(string path, int barPeriod, int postPeriod, int precision = 2, string description = "")
        {
            var split = path.Split('/');
            var name = split.LastOrDefault();
            var nodePath = path.Substring(0, path.Length - name.Length - 1);

            var options = new BarSensorOptions()
            {
                SensorName = name,
                NodePath = nodePath,
                Precision = precision,
                CollectBarPeriod = TimeSpan.FromMilliseconds(barPeriod),
                PostDataPeriod = TimeSpan.FromMilliseconds(postPeriod),
            };

            return CreateBarSensor(new DoubleBarPublicSensor(options));
        }

        public IBarSensor<double> Create1HrDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 3600000, 15000, precision, description);

        public IBarSensor<double> Create30MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 1800000, 15000, precision, description);

        public IBarSensor<double> Create10MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 600000, 15000, precision, description);

        public IBarSensor<double> Create5MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 300000, 15000, precision, description);

        public IBarSensor<double> Create1MinDoubleBarSensor(string path, int precision = 2, string description = "") => CreateDoubleBarSensor(path, 60000, 15000, precision, description);


        private IBarSensor<T> CreateBarSensor<BarType, T>(PublicBarMonitoringSensor<BarType, T> newSensor)
            where BarType : MonitoringBarBase<T>, new()
            where T : struct
        {
            if (_sensorsStorage.TryGetValue(newSensor.SensorPath, out var sensor))
                return (IBarSensor<T>)sensor;

            if (Status.IsRunning())
            {
                _ = _sensorsStorage.Run(newSensor);
                return newSensor;
            }

            return (IBarSensor<T>)_sensorsStorage.Register(newSensor);
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

            OneParamFuncSensor<T, U> sensor = new OneParamFuncSensor<T, U>(path, _dataQueue as IValuesQueue, description, interval, function, _logger);
            AddNewSensor(sensor, path);

            return sensor;
        }

        private INoParamsFuncSensor<T> CreateNoParamsFuncSensorInternal<T>(string path, string description, Func<T> function,
            TimeSpan interval)
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is INoParamsFuncSensor<T> typedSensor)
                return typedSensor;

            NoParamsFuncSensor<T> sensor = new NoParamsFuncSensor<T>(path, _dataQueue as IValuesQueue, description, interval, function, _logger);
            AddNewSensor(sensor, path);

            return sensor;
        }

        #endregion

        private SensorBase GetExistingSensor(string path)
        {
            if (_sensorsStorage.ContainsKey(path))
            {
                var message = $"Path {path} is used by standard performance sensor!";
                _logger.Error(message);

                throw new InvalidSensorPathException(message);
            }

            if (_nameToSensor.TryGetValue(path, out var readValue))
                return readValue as SensorBase;

            return null;
        }

        private void AddNewSensor(ISensor sensor, string path)
        {
            _nameToSensor[path] = sensor;

            _logger.Info($"Added new sensor {path}");
        }
    }
}