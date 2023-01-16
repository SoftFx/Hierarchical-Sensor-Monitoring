using HSMDataCollector.Bar;
using HSMDataCollector.Base;
using HSMDataCollector.CustomFuncSensor;
using HSMDataCollector.DefaultValueSensor;
using HSMDataCollector.Exceptions;
using HSMDataCollector.InstantValue;
using HSMDataCollector.Logging;
using HSMDataCollector.PerformanceSensor.Base;
using HSMDataCollector.PerformanceSensor.ProcessMonitoring;
using HSMDataCollector.PerformanceSensor.SystemMonitoring;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace HSMDataCollector.Core
{
    /// <summary>
    /// Main monitoring class which is used to create and control sensors' instances
    /// </summary>
    public sealed class DataCollector : IDataCollector
    {
        private const string LogDefaultFolder = "Logs";
        private const string LogFormatFileName = "DataCollector_${shortdate}.log";

        private const string ServiceAlive = "Service alive";

        internal const string CurrentProcessNodeName = "CurrentProcess";
        internal const string PerformanceNodeName = "System monitoring";

        private readonly string _productKey;
        private readonly string _listSendingAddress;
        private readonly string _fileSendingAddress;
        private readonly ConcurrentDictionary<string, ISensor> _nameToSensor;
        private readonly HttpClient _client;
        private readonly IDataQueue _dataQueue;

        private NLog.Logger _logger;
        private bool _isStopped;
        private bool _isLogging;

        public event EventHandler ValuesQueueOverflow;


        /// <summary>
        /// Creates new instance of <see cref="DataCollector"/> class, initializing main parameters
        /// </summary>
        /// <param name="productKey">Key, which identifies the product (logical group) for all sensors that will be created.</param>
        /// <param name="address">HSM server address to send data to (Do not forget https:// if needed)</param>
        /// <param name="port">HSM sensors API port, which defaults to 44330. Specify if your HSM server Docker container configured differently.</param>
        public DataCollector(string productKey, string address, int port = 44330)
        {
            var connectionAddress = $"{address}:{port}/api/sensors";
            _listSendingAddress = $"{connectionAddress}/list";
            _fileSendingAddress = $"{connectionAddress}/file";
            _productKey = productKey;
            _nameToSensor = new ConcurrentDictionary<string, ISensor>();

            HttpClientHandler handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true
            };
            _client = new HttpClient(handler);
            _client.DefaultRequestHeaders.Add(nameof(BaseRequest.Key), productKey);

            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            _dataQueue = new DataQueue();
            _dataQueue.QueueOverflow += DataQueue_QueueOverflow;
            _dataQueue.FileReceving += DataQueue_FileReceving;
            _dataQueue.SendValues += DataQueue_SendValues;
            _isStopped = false;
        }


        public void Dispose()
        {
            Stop();
        }

        public void Initialize(bool useLogging = true, string folderPath = null, string fileNameFormat = null)
        {
            if (useLogging)
            {
                _logger = Logger.Create(nameof(DataCollector));

                Logger.UpdateFilePath(folderPath ?? $"{AppDomain.CurrentDomain.BaseDirectory}/{LogDefaultFolder}",
                                      fileNameFormat ?? LogFormatFileName);

                _isLogging = true;
            }

            _logger?.Info("Initialize timer...");
            _dataQueue.InitializeTimer();
        }

        [Obsolete("Use Initialize(bool, string, string)")]
        public void Initialize()
        {
            _dataQueue.InitializeTimer();
        }

        public void Stop()
        {
            if (_isStopped)
                return;

            _logger?.Info("DataCollector stopping...");

            var allData = new List<SensorValueBase>(1 << 3);
            if (_dataQueue != null)
            {
                allData.AddRange(_dataQueue.GetCollectedData());
                _dataQueue.Stop();
            }

            Thread.Sleep(TimeSpan.FromSeconds(1));

            foreach (var pair in _nameToSensor)
                if (pair.Value.HasLastValue)
                    allData.Add(pair.Value.GetLastValue());

            foreach (var pair in _nameToSensor)
                pair.Value.Dispose();

            if (allData.Count != 0)
                SendMonitoringData(allData);

            _client?.Dispose();
            _isStopped = true;
            _logger?.Info("DataCollector successfully stopped.");
        }

        public void InitializeSystemMonitoring(bool isCPU, bool isFreeRam, string specificPath = null)
        {
            StartSystemMonitoring(isCPU, isFreeRam, specificPath);
        }

        public void InitializeProcessMonitoring(bool isCPU, bool isMemory, bool isThreads, string specificPath = null)
        {
            StartCurrentProcessMonitoring(isCPU, isMemory, isThreads, specificPath);
        }

        [Obsolete("Method has no implementation")]
        public void InitializeProcessMonitoring(string processName, bool isCPU, bool isMemory, bool isThreads, string specificPath = null)
        {

        }

        public void InitializeOsMonitoring(bool isUpdated, string specificPath = null)
        {
            if (isUpdated)
                InitializeWindowsUpdateMonitoring(new TimeSpan(24, 0, 0), new TimeSpan(30, 0, 0, 0), specificPath);
        }

        public void MonitorServiceAlive(string specificPath = null)
        {
            var path = $"{specificPath ?? PerformanceNodeName}/{ServiceAlive}";

            _logger?.Info($"Initialize {path} sensor...");

            NoParamsFuncSensor<bool> aliveSensor = new NoParamsFuncSensor<bool>(path,
                _dataQueue as IValuesQueue, string.Empty, TimeSpan.FromSeconds(15),
                SensorType.BooleanSensor, () => true, _isLogging);
            AddNewSensor(aliveSensor, aliveSensor.Path);
        }

        public bool InitializeWindowsUpdateMonitoring(TimeSpan sensorInterval, TimeSpan updateInterval, string specificPath = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger?.Error($"Failed to create {nameof(WindowsUpdateFuncSensor)} because current OS is not Windows");
                return false;
            }

            _logger?.Info($"Initialize windows update sensor...");

            var updateSensor = new WindowsUpdateFuncSensor(specificPath,
                _dataQueue as IValuesQueue, string.Empty, sensorInterval,
                SensorType.BooleanSensor, _isLogging, updateInterval);
            AddNewSensor(updateSensor, updateSensor.Path);

            return true;
        }

        public bool IsSensorExists(string path) => _nameToSensor.ContainsKey(path);

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

            OneParamFuncSensor<T, U> sensor = new OneParamFuncSensor<T, U>(path, _dataQueue as IValuesQueue, description,
                interval, GetSensorType(typeof(T)), function, _isLogging);
            AddNewSensor(sensor, path);

            return sensor;
        }

        private INoParamsFuncSensor<T> CreateNoParamsFuncSensorInternal<T>(string path, string description, Func<T> function,
            TimeSpan interval)
        {
            var existingSensor = GetExistingSensor(path);
            if (existingSensor is INoParamsFuncSensor<T> typedSensor)
                return typedSensor;

            NoParamsFuncSensor<T> sensor = new NoParamsFuncSensor<T>(path, _dataQueue as IValuesQueue, description,
                interval, GetSensorType(typeof(T)), function, _isLogging);
            AddNewSensor(sensor, path);

            return sensor;
        }

        private static SensorType GetSensorType(Type type)
        {
            if (type == typeof(int))
                return SensorType.IntSensor;

            if (type == typeof(double))
                return SensorType.DoubleSensor;

            if (type == typeof(bool))
                return SensorType.BooleanSensor;

            return SensorType.StringSensor;
        }

        #endregion

        private void StartSystemMonitoring(bool isCPU, bool isFreeRam, string specificPath)
        {
            if (isCPU)
            {
                TotalCPUSensor cpuSensor = new TotalCPUSensor(_dataQueue as IValuesQueue, specificPath);
                AddNewSensor(cpuSensor, cpuSensor.Path);
            }

            if (isFreeRam)
            {
                FreeMemorySensor freeMemorySensor = new FreeMemorySensor(_dataQueue as IValuesQueue, specificPath);
                AddNewSensor(freeMemorySensor, freeMemorySensor.Path);
            }
        }

        private void StartCurrentProcessMonitoring(bool isCPU, bool isMemory, bool isThreads, string specificPath)
        {
            Process currentProcess = Process.GetCurrentProcess();
            if (isCPU)
            {
                ProcessCPUSensor currentCpuSensor = new ProcessCPUSensor(_dataQueue as IValuesQueue, currentProcess.ProcessName, specificPath);
                AddNewSensor(currentCpuSensor, currentCpuSensor.Path);
            }

            if (isMemory)
            {
                ProcessMemorySensor currentMemorySensor = new ProcessMemorySensor(_dataQueue as IValuesQueue, currentProcess.ProcessName, specificPath);
                AddNewSensor(currentMemorySensor, currentMemorySensor.Path);
            }

            if (isThreads)
            {
                ProcessThreadCountSensor currentThreadCount = new ProcessThreadCountSensor(_dataQueue as IValuesQueue, currentProcess.ProcessName, specificPath);
                AddNewSensor(currentThreadCount, currentThreadCount.Path);
            }
        }

        private SensorBase GetExistingSensor(string path)
        {
            SensorBase sensor = null;
            if (_nameToSensor.TryGetValue(path, out var readValue))
                sensor = readValue as SensorBase;

            if (sensor == null && (sensor as IPerformanceSensor) != null)
            {
                var message = $"Path {path} is used by standard performance sensor!";
                _logger?.Error(message);
                throw new InvalidSensorPathException(message);
            }

            return sensor;
        }

        private void AddNewSensor(ISensor sensor, string path)
        {
            _nameToSensor[path] = sensor;

            _logger?.Info($"Added new sensor {path}");
        }

        private void DataQueue_SendValues(object sender, List<SensorValueBase> e)
        {
            SendMonitoringData(e);
        }

        private void DataQueue_FileReceving(object _, FileSensorValue value)
        {
            try
            {
                string jsonString = JsonConvert.SerializeObject(value);
                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var res = _client.PostAsync(_fileSendingAddress, data).Result;

                if (!res.IsSuccessStatusCode)
                    _logger?.Error($"Failed to send data. StatusCode={res.StatusCode}, Content={res.Content.ReadAsStringAsync().Result}");
            }
            catch (Exception e)
            {
                if (_dataQueue != null && !_dataQueue.Disposed)
                    _dataQueue?.ReturnFile(value);

                _logger?.Error($"Failed to send: {e}");
            }
        }

        private void SendMonitoringData(List<SensorValueBase> values)
        {
            try
            {
                if (values.Count == 0)
                    return;

                string jsonString = JsonConvert.SerializeObject(values.Cast<object>());
                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var res = _client.PostAsync(_listSendingAddress, data).Result;

                if (!res.IsSuccessStatusCode)
                    _logger?.Error($"Failed to send data. StatusCode={res.StatusCode}, Content={res.Content.ReadAsStringAsync().Result}");
            }
            catch (Exception e)
            {
                if (_dataQueue != null && !_dataQueue.Disposed)
                    _dataQueue?.ReturnData(values);

                _logger?.Error($"Failed to send: {e}");
            }
        }

        private void DataQueue_QueueOverflow(object sender, DateTime e)
        {
            OnValuesQueueOverflow();
        }

        private void OnValuesQueueOverflow()
        {
            ValuesQueueOverflow?.Invoke(this, EventArgs.Empty);
        }
    }
}
