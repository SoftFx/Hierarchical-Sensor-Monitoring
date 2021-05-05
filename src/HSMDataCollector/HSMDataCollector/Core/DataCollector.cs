using HSMDataCollector.Bar;
using HSMDataCollector.Base;
using HSMDataCollector.Exceptions;
using HSMDataCollector.InstantValue;
using HSMDataCollector.PerformanceSensor.Base;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using HSMDataCollector.DefaultValueSensor;
using HSMDataCollector.PerformanceSensor.CustomFuncSensor;
using HSMDataCollector.PerformanceSensor.ProcessMonitoring;
using HSMDataCollector.PerformanceSensor.SystemMonitoring;
using Newtonsoft.Json;
using HSMDataCollector.Logging;

namespace HSMDataCollector.Core
{
    public class DataCollector : IDataCollector
    {
        private readonly string _productKey;
        private readonly string _listSendingAddress;
        private readonly ConcurrentDictionary<string, ISensor> _nameToSensor;
        private readonly HttpClient _client;
        private readonly IDataQueue _dataQueue;
        private NLog.Logger _logger;
        public DataCollector(string productKey, string address, int port)
        {
            var connectionAddress = $"{address}:{port}/api/sensors";
            _listSendingAddress = $"{connectionAddress}/list";
            _productKey = productKey;
            _nameToSensor = new ConcurrentDictionary<string, ISensor>();
            _client = new HttpClient();
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            _dataQueue = new DataQueue();
            _dataQueue.QueueOverflow += DataQueue_QueueOverflow;
            _dataQueue.SendData += DataQueue_SendData;
        }

        public event EventHandler ValuesQueueOverflow;
        //Use after constructor!
        public void Initialize(bool useLogging = true, string folderPath = null, string fileNameFormat = null)
        {
            if (useLogging)
            {
                _logger = Logger.Create(nameof(DataCollector));

                Logger.UpdateFilePath(folderPath ?? $"{AppDomain.CurrentDomain.BaseDirectory}/{TextConstants.LogDefaultFolder}",
                    fileNameFormat ?? TextConstants.LogFormatFileName);
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
            _logger?.Info("DataCollector stopping...");

            List<CommonSensorValue> allData = new List<CommonSensorValue>();
            if (_dataQueue != null)
            {
                allData.AddRange(_dataQueue.GetAllCollectedData());
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
                SendData(allData);
            }
            
            _client.Dispose();
            _logger?.Info("DataCollector successfully stopped.");
        }
        public void InitializeSystemMonitoring(bool isCPU, bool isFreeRam)
        {
            StartSystemMonitoring(isCPU, isFreeRam);
        }

        public void InitializeProcessMonitoring(bool isCPU, bool isMemory, bool isThreads)
        {
            StartCurrentProcessMonitoring(isCPU, isMemory, isThreads);
        }

        public void InitializeProcessMonitoring(string processName, bool isCPU, bool isMemory, bool isThreads)
        {
            
        }

        public void MonitorServiceAlive()
        {
            string path = $"{TextConstants.PerformanceNodeName}/Service alive";
            _logger?.Info($"Initialize {path} sensor...");

            BoolFuncSensor aliveSensor = new BoolFuncSensor(() => true, path, _productKey,
                _dataQueue as IValuesQueue);
            AddNewSensor(aliveSensor, path);
        }

        public IBoolSensor CreateBoolSensor(string path)
        {
            var existingSensor = GetExistingSensor(path);
            var boolSensor = existingSensor as IBoolSensor;
            if (boolSensor != null)
            {
                return boolSensor;
            }
            
            InstantValueSensorBool sensor = new InstantValueSensorBool(path, _productKey, _dataQueue as IValuesQueue);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public IDoubleSensor CreateDoubleSensor(string path)
        {
            var existingSensor = GetExistingSensor(path);
            var doubleSensor = existingSensor as IDoubleSensor;
            if (doubleSensor != null)
            {
                return doubleSensor;
            }

            InstantValueSensorDouble sensor = new InstantValueSensorDouble(path, _productKey, _dataQueue as IValuesQueue);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public IIntSensor CreateIntSensor(string path)
        {
            var existingSensor = GetExistingSensor(path);
            var intSensor = existingSensor as IIntSensor;
            if (intSensor != null)
            {
                return intSensor;
            }

            InstantValueSensorInt sensor = new InstantValueSensorInt(path, _productKey, _dataQueue as IValuesQueue);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public IStringSensor CreateStringSensor(string path)
        {
            var existingSensor = GetExistingSensor(path);
            var stringSensor = existingSensor as IStringSensor;
            if (stringSensor != null)
            {
                return stringSensor;
            }
            
            InstantValueSensorString sensor = new InstantValueSensorString(path, _productKey, _dataQueue as IValuesQueue);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public IDefaultValueSensorInt CreateDefaultValueSensorInt(string path, int defaultValue)
        {
            var existingSensor = GetExistingSensor(path);
            var defaultValueSensorInt = existingSensor as IDefaultValueSensorInt;
            if (defaultValueSensorInt != null)
            {
                return defaultValueSensorInt;
            }

            DefaultValueSensorInt sensor = new DefaultValueSensorInt(path, _productKey, _dataQueue as IValuesQueue, defaultValue);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public IDefaultValueSensorDouble CreateDefaultValueSensorDouble(string path, double defaultValue)
        {
            var existingSensor = GetExistingSensor(path);
            var defaultValueSensorDouble = existingSensor as IDefaultValueSensorDouble;
            if (defaultValueSensorDouble != null)
            {
                return defaultValueSensorDouble;
            }

            DefaultValueSensorDouble sensor = new DefaultValueSensorDouble(path, _productKey, _dataQueue as IValuesQueue, defaultValue);
            AddNewSensor(sensor, path);
            return sensor;
        }

        #region Bar sensors

        public IDoubleBarSensor Create1HrDoubleBarSensor(string path, int precision = 2)
        {
            return CreateDoubleBarSensor(path, 3600000, 15000, precision);
        }

        public IDoubleBarSensor Create30MinDoubleBarSensor(string path, int precision = 2)
        {
            return CreateDoubleBarSensor(path, 1800000, 15000, precision);
        }

        public IDoubleBarSensor Create10MinDoubleBarSensor(string path, int precision = 2)
        {
            return CreateDoubleBarSensor(path, 600000, 15000, precision);
        }

        public IDoubleBarSensor Create5MinDoubleBarSensor(string path, int precision = 2)
        {
            return CreateDoubleBarSensor(path, 300000, 15000, precision);
        }

        public IDoubleBarSensor Create1MinDoubleBarSensor(string path, int precision = 2)
        {
            return CreateDoubleBarSensor(path, 60000, 15000, precision);
        }
        public IDoubleBarSensor CreateDoubleBarSensor(string path, int timeout = 30000, int smallPeriod = 15000, int precision = 2)
        {
            var existingSensor = GetExistingSensor(path);
            var doubleBarSensor = existingSensor as IDoubleBarSensor;
            if (doubleBarSensor != null)
            {
                (doubleBarSensor as BarSensorBase)?.Restart(timeout, smallPeriod);
                return doubleBarSensor;
            }

            BarSensorDouble sensor = new BarSensorDouble(path, _productKey, _dataQueue as IValuesQueue, timeout, smallPeriod, precision);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public IIntBarSensor Create1HrIntBarSensor(string path)
        {
            return CreateIntBarSensor(path, 3600000);
        }

        public IIntBarSensor Create30MinIntBarSensor(string path)
        {
            return CreateIntBarSensor(path, 1800000);
        }

        public IIntBarSensor Create10MinIntBarSensor(string path)
        {
            return CreateIntBarSensor(path, 600000);
        }

        public IIntBarSensor Create5MinIntBarSensor(string path)
        {
            return CreateIntBarSensor(path, 300000);
        }

        public IIntBarSensor Create1MinIntBarSensor(string path)
        {
            return CreateIntBarSensor(path, 60000);
        }

        public IIntBarSensor CreateIntBarSensor(string path, int timeout = 30000, int smallPeriod = 15000)
        {
            var existingSensor = GetExistingSensor(path);
            var intBarSensor = existingSensor as IIntBarSensor;
            if (intBarSensor != null)
            {
                (intBarSensor as BarSensorBase)?.Restart(timeout, smallPeriod);
                return intBarSensor;
            }

            BarSensorInt sensor = new BarSensorInt(path, _productKey, _dataQueue as IValuesQueue, timeout, smallPeriod);
            AddNewSensor(sensor, path);
            return sensor;
        }

        #endregion


        public int GetSensorCount()
        {
            return GetCount();
        }

        private void StartSystemMonitoring(bool isCPU, bool isFreeRam)
        {
            if (isCPU)
            {
                CPUSensor cpuSensor = new CPUSensor(_productKey, _dataQueue as IValuesQueue);
                AddNewSensor(cpuSensor, cpuSensor.Path);
            }

            if (isFreeRam)
            {
                FreeMemorySensor freeMemorySensor = new FreeMemorySensor(_productKey, _dataQueue as IValuesQueue);
                AddNewSensor(freeMemorySensor, freeMemorySensor.Path);
            }
            //FreeDiskSpaceSensor freeDiskSpaceSensor = new FreeDiskSpaceSensor();
        }

        private void StartCurrentProcessMonitoring(bool isCPU, bool isMemory, bool isThreads)
        {
            Process currentProcess = Process.GetCurrentProcess();
            if (isCPU)
            {
                ProcessCPUSensor currentCpuSensor = new ProcessCPUSensor(_productKey, _dataQueue as IValuesQueue, currentProcess.ProcessName);
                AddNewSensor(currentCpuSensor, currentCpuSensor.Path);
            }

            if (isMemory)
            {
                ProcessMemorySensor currentMemorySensor = new ProcessMemorySensor(_productKey, _dataQueue as IValuesQueue, currentProcess.ProcessName);
                AddNewSensor(currentMemorySensor, currentMemorySensor.Path);
            }

            if (isThreads)
            {
                ProcessThreadCountSensor currentThreadCount = new ProcessThreadCountSensor(_productKey, _dataQueue as IValuesQueue, currentProcess.ProcessName);
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

        private void DataQueue_SendData(object sender, List<CommonSensorValue> e)
        {
            SendData(e);
        }

        private void SendData(List<CommonSensorValue> values)
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
                    _logger?.Error($"Failed to send data. StatusCode={res.StatusCode}, Content={res.Content}");
                }
            }
            catch (Exception e)
            {
                if (_dataQueue != null && !_dataQueue.Disposed)
                {
                    _dataQueue?.ReturnFailedData(values);
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
