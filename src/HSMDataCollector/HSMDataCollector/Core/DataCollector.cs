﻿using HSMDataCollector.Bar;
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
using HSMSensorDataObjects.FullDataObject;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    public class DataCollector : IDataCollector
    {
        private readonly string _productKey;
        private readonly string _listSendingAddress;
        private readonly ConcurrentDictionary<string, ISensor> _nameToSensor;
        private readonly HttpClient _client;
        private readonly IDataQueue _dataQueue;
        private NLog.Logger _logger;
        private bool _isStopped;
        private bool _isLogging;

        /// <summary>
        /// Creates new instance of <see cref="DataCollector"/> class, initializing main parameters
        /// </summary>
        /// <param name="productKey">Key, which identifies the product (logical group) for all sensors that will be created.</param>
        /// <param name="address">HSM server address to send data to (Do not forget https:// if needed)</param>
        /// <param name="port">HSM sensors API port, which defaults to 44330. Specify if your HSM server Docker container configured differently.</param>
        public DataCollector(string productKey, string address, int port = 44330)
        {
            var connectionAddress = $"{address}:{port}/api/sensors";
            _listSendingAddress = $"{connectionAddress}/listNew";
            _productKey = productKey;
            _nameToSensor = new ConcurrentDictionary<string, ISensor>();
            HttpClientHandler handler = new HttpClientHandler();
            handler.ServerCertificateCustomValidationCallback = (message, certificate2, arg3, arg4) => true;
            _client = new HttpClient(handler);
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            _dataQueue = new DataQueue();
            _dataQueue.QueueOverflow += DataQueue_QueueOverflow;
            _dataQueue.SendValues += DataQueue_SendValues;
            _isStopped = false;
        }

        public event EventHandler ValuesQueueOverflow;
        public void Initialize(bool useLogging = true, string folderPath = null, string fileNameFormat = null)
        {
            if (useLogging)
            {
                _logger = Logger.Create(nameof(DataCollector));

                Logger.UpdateFilePath(folderPath ?? $"{AppDomain.CurrentDomain.BaseDirectory}/{TextConstants.LogDefaultFolder}",
                    fileNameFormat ?? TextConstants.LogFormatFileName);
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

            List<UnitedSensorValue> allData = new List<UnitedSensorValue>();
            if (_dataQueue != null)
            {
                //allData.AddRange(_dataQueue.GetAllCollectedData());
                allData.AddRange(_dataQueue.GetCollectedData());
                _dataQueue.Stop();
            }

            Thread.Sleep(TimeSpan.FromSeconds(1));
            
            foreach (var pair in _nameToSensor)
            {
                if (pair.Value.HasLastValue)
                {
                    allData.Add(pair.Value.GetLastValue());
                }
            }
            foreach (var pair in _nameToSensor)
            {
                pair.Value.Dispose();
            }


            if (allData.Any())
            {
                //SendData(allData);
                SendMonitoringData(allData);
            }
            
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

        public void InitializeProcessMonitoring(string processName, bool isCPU, bool isMemory, bool isThreads, string specificPath = null)
        {
            
        }
        public void InitializeOsMonitoring(bool isUpdated, string specificPath = null)
        {
            if (isUpdated)
                InitializeWindowsUpdateMonitoring(new TimeSpan(24,0,0), new TimeSpan(30,0,0,0), specificPath);
        }

        public void MonitorServiceAlive(string specificPath)
        {
            var path = $"{specificPath ?? TextConstants.PerformanceNodeName}/{TextConstants.ServiceAlive}";

           _logger?.Info($"Initialize {path} sensor...");

            NoParamsFuncSensor<bool> aliveSensor = new NoParamsFuncSensor<bool>(path, 
                _productKey, _dataQueue as IValuesQueue, string.Empty, TimeSpan.FromSeconds(15),
                SensorType.BooleanSensor, () => true,_isLogging);
            AddNewSensor(aliveSensor, aliveSensor.Path);
        }

        public bool InitializeWindowsUpdateMonitoring(TimeSpan sensorInterval, TimeSpan updateInterval, string specificPath = null)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                _logger?.Error($"Failed to create {nameof(WindowsUpdateFuncSensor)} " +
                    $"because current OS is not Windows");
                return false;
            }

            _logger?.Info($"Initialize windows update sensor...");

            var updateSensor = new WindowsUpdateFuncSensor(specificPath,
                _productKey, _dataQueue as IValuesQueue, string.Empty, sensorInterval,
                SensorType.BooleanSensor, _isLogging, updateInterval);

            AddNewSensor(updateSensor, updateSensor.Path);
            return true;
        }

        public bool IsSensorExists(string path) => _nameToSensor.ContainsKey(path);

        #region Generic sensors functionality

        public IInstantValueSensor<bool> CreateBoolSensor(string path, string description)
        {
            return CreateInstantValueSensorInternal<bool>(path, description, SensorType.BooleanSensor);
        }

        public IInstantValueSensor<int> CreateIntSensor(string path, string description)
        {
            return CreateInstantValueSensorInternal<int>(path, description, SensorType.IntSensor);
        }

        public IInstantValueSensor<double> CreateDoubleSensor(string path, string description)
        {
            return CreateInstantValueSensorInternal<double>(path, description, SensorType.DoubleSensor);
        }

        public IInstantValueSensor<string> CreateStringSensor(string path, string description)
        {
            return CreateInstantValueSensorInternal<string>(path, description, SensorType.StringSensor);
        }

        private IInstantValueSensor<T> CreateInstantValueSensorInternal<T>(string path, string description, 
            SensorType sensorType)
        {
            var existingSensor = GetExistingSensor(path);
            var instantValueSensor = existingSensor as IInstantValueSensor<T>;
            if (instantValueSensor != null)
            {
                return instantValueSensor;
            }

            InstantValueSensor<T> sensor = new InstantValueSensor<T>(path, _productKey, _dataQueue as IValuesQueue,
                sensorType, description);
            AddNewSensor(sensor, path);
            return sensor;
        }
        public ILastValueSensor<bool> CreateLastValueBoolSensor(string path, bool defaultValue, string description = "")
        {
            return CreateLastValueSensorInternal(path, defaultValue, SensorType.BooleanSensor, description);
        }

        public ILastValueSensor<int> CreateLastValueIntSensor(string path, int defaultValue, string description = "")
        {
            return CreateLastValueSensorInternal(path, defaultValue, SensorType.IntSensor, description);
        }

        public ILastValueSensor<double> CreateLastValueDoubleSensor(string path, double defaultValue, string description = "")
        {
            return CreateLastValueSensorInternal(path, defaultValue, SensorType.DoubleSensor, description);
        }

        public ILastValueSensor<string> CreateLastValueStringSensor(string path, string defaultValue, string description = "")
        {
            return CreateLastValueSensorInternal(path, defaultValue, SensorType.StringSensor, description);
        }

        private ILastValueSensor<T> CreateLastValueSensorInternal<T>(string path, T defaultValue, SensorType type,
            string description = "")
        {
            var existingSensor = GetExistingSensor(path);
            var lastValueSensor = existingSensor as ILastValueSensor<T>;
            if (lastValueSensor != null)
            {
                return lastValueSensor;
            }

            DefaultValueSensor<T> sensor =
                new DefaultValueSensor<T>(path, _productKey, _dataQueue as IValuesQueue, type, defaultValue, description);
            AddNewSensor(sensor, path);
            return sensor;
        }

        #endregion

        #region Generic bar sensors

        public IBarSensor<int> CreateIntBarSensor(string path, int timeout, int smallPeriod = 15000, string description = "")
        {
            var existingSensor = GetExistingSensor(path);
            var intBarSensor = existingSensor as IBarSensor<int>;
            if (intBarSensor != null)
            {
                (intBarSensor as BarSensorBase)?.Restart(timeout, smallPeriod);
                return intBarSensor;
            }

            BarSensor<int> sensor = new BarSensor<int>(path, _productKey, _dataQueue as IValuesQueue, SensorType.IntegerBarSensor,
                timeout, smallPeriod,  description);
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
            var doubleBarSensor = existingSensor as IBarSensor<double>;
            if (doubleBarSensor != null)
            {
                (doubleBarSensor as BarSensorBase)?.Restart(timeout, smallPeriod);
                return doubleBarSensor;
            }

            BarSensor<double> sensor = new BarSensor<double>(path, _productKey, _dataQueue as IValuesQueue,
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
            return CreateParamsFuncSensorInternal(path, description, function,
                TimeSpan.FromMilliseconds(millisecondsInterval));
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
            var typedSensor = existingSensor as IParamsFuncSensor<T, U>;
            if (typedSensor != null)
            {
                return typedSensor;
            }

            OneParamFuncSensor<T, U> sensor = new OneParamFuncSensor<T, U>(path, _productKey, _dataQueue as IValuesQueue, description,
                interval, GetSensorType(typeof(T)), function, _isLogging);
            AddNewSensor(sensor, path);
            return sensor;
        }
        private INoParamsFuncSensor<T> CreateNoParamsFuncSensorInternal<T>(string path, string description, Func<T> function,
            TimeSpan interval)
        {
            var existingSensor = GetExistingSensor(path);
            var typedSensor = existingSensor as INoParamsFuncSensor<T>;
            if (typedSensor != null)
            {
                return typedSensor;
            }

            NoParamsFuncSensor<T> sensor = new NoParamsFuncSensor<T>(path, _productKey, _dataQueue as IValuesQueue, description,
                interval, GetSensorType(typeof(T)), function, _isLogging);
            AddNewSensor(sensor, path);
            return sensor;
        }

        private SensorType GetSensorType(Type type)
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
        public int GetSensorCount()
        {
            return GetCount();
        }

        private void StartSystemMonitoring(bool isCPU, bool isFreeRam, string specificPath)
        {
            if (isCPU)
            {
                TotalCPUSensor cpuSensor = new TotalCPUSensor(_productKey, _dataQueue as IValuesQueue, specificPath);
                AddNewSensor(cpuSensor, cpuSensor.Path);
            }

            if (isFreeRam)
            {
                FreeMemorySensor freeMemorySensor = new FreeMemorySensor(_productKey, _dataQueue as IValuesQueue, specificPath);
                AddNewSensor(freeMemorySensor, freeMemorySensor.Path);
            }
            //FreeDiskSpaceSensor freeDiskSpaceSensor = new FreeDiskSpaceSensor();
        }

        private void StartCurrentProcessMonitoring(bool isCPU, bool isMemory, bool isThreads, string specificPath)
        {
            Process currentProcess = Process.GetCurrentProcess();
            if (isCPU)
            {
                ProcessCPUSensor currentCpuSensor = new ProcessCPUSensor(_productKey, _dataQueue as IValuesQueue, currentProcess.ProcessName, specificPath);
                AddNewSensor(currentCpuSensor, currentCpuSensor.Path);
            }

            if (isMemory)
            {
                ProcessMemorySensor currentMemorySensor = new ProcessMemorySensor(_productKey, _dataQueue as IValuesQueue, currentProcess.ProcessName, specificPath);
                AddNewSensor(currentMemorySensor, currentMemorySensor.Path);
            }

            if (isThreads)
            {
                ProcessThreadCountSensor currentThreadCount = new ProcessThreadCountSensor(_productKey, _dataQueue as IValuesQueue, currentProcess.ProcessName, specificPath);
                AddNewSensor(currentThreadCount, currentThreadCount.Path);
            }
        }
        private void StartProcessMonitoring(string processFileName)
        {
            string instanceName = BuildInstanceName(processFileName);
        }

        private string BuildInstanceName(string processFileName)
        {
            string processName = Path.GetFileNameWithoutExtension(processFileName);

            if (string.IsNullOrEmpty(processName))
            {
                return string.Empty;
            }

            Process[] instances = Process.GetProcessesByName(processName);
            if (!instances.Any())
            {
                return string.Empty;
            }

            for (int i = 0; i < instances.Length; i++)
            {
                if (processFileName.Equals(instances[i].MainModule.FileName))
                {
                    if (i.Equals(0))
                        return processName;

                    return $"{processName}#{i}";
                }
            }

            return string.Empty;
        }

        private SensorBase GetExistingSensor(string path)
        {
            SensorBase sensor = null;
            bool exists = _nameToSensor.TryGetValue(path, out var readValue);
            if (exists)
            {
                sensor = readValue as SensorBase;
            }

            if (sensor == null && sensor as IPerformanceSensor != null)
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
            //_nameToSensor.AddOrUpdate(path, sensor);
        }
        private int GetCount()
        {
            int count = 0;
            count = _nameToSensor.Count;

            return count;
        }
        private void DataQueue_SendValues(object sender, List<UnitedSensorValue> e)
        {
            SendMonitoringData(e);
        }
        //private void DataQueue_SendData(object sender, List<CommonSensorValue> e)
        //{
        //    SendData(e);
        //}

        private void SendMonitoringData(List<UnitedSensorValue> values)
        {
            try
            {
                string jsonString = JsonConvert.SerializeObject(values);
                //_logger?.Info("Try to send data: " + jsonString);
                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var res = _client.PostAsync(_listSendingAddress, data).Result;
                if (res.IsSuccessStatusCode)
                {
                    //_logger?.Info("Data successfully sent.");
                }
                else
                {
                    _logger?.Error($"Failed to send data. StatusCode={res.StatusCode}, Content={res.Content.ReadAsStringAsync().Result}");
                }
            }
            catch (Exception e)
            {
                if (_dataQueue != null && !_dataQueue.Disposed)
                {
                    _dataQueue?.ReturnData(values);
                }

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

        public void Dispose()
        {
            Stop();
        }

    }
}
