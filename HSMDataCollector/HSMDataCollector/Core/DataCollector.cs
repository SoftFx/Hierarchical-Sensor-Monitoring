using HSMDataCollector.Bar;
using HSMDataCollector.Base;
using HSMDataCollector.Exceptions;
using HSMDataCollector.InstantValue;
using HSMDataCollector.PerformanceSensor.Base;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
//using System.Text.Json;
using HSMDataCollector.DefaultValueSensor;
using HSMDataCollector.PerformanceSensor.ProcessMonitoring;
using HSMDataCollector.PerformanceSensor.SystemMonitoring;
using Newtonsoft.Json;

namespace HSMDataCollector.Core
{
    public class DataCollector : IDataCollector
    {
        private readonly string _productKey;
        private readonly string _listSendingAddress;
        private readonly Dictionary<string, ISensor> _nameToSensor;
        private readonly object _syncRoot = new object();
        private readonly HttpClient _client;
        private readonly IDataQueue _dataQueue;
        //private readonly List<>
        //private readonly InstantValueSensorInt _countSensor;
        public DataCollector(string productKey, string address, int port)
        {
            var connectionAddress = $"{address}:{port}/api/sensors";
            _listSendingAddress = $"{connectionAddress}/list";
            _productKey = productKey;
            _nameToSensor = new Dictionary<string, ISensor>();
            _client = new HttpClient();
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            _dataQueue = new DataQueue();
            _dataQueue.QueueOverflow += DataQueue_QueueOverflow;
            _dataQueue.SendData += DataQueue_SendData;
            //_countSensor = new InstantValueSensorInt("CountSensor", productKey, address);
        }

        //private ISensor CreateNewSensor(string path, SensorType type)
        //{
        //    switch (type)
        //    {
        //        case SensorType.BooleanValue:
        //            return new InstantValueSensorBool(path, _productKey, _connectionAddress);
        //        case SensorType.IntValue:
        //            return new InstantValueSensorInt(path, _productKey, _connectionAddress);
        //        case SensorType.DoubleValue:
        //            return new InstantValueSensorDouble(path, _productKey, _connectionAddress);
        //        case SensorType.StringValue:
        //            return new InstantValueSensorString(path, _productKey, _connectionAddress);
        //        case SensorType.IntegerBar:
        //            return new BarSensorInt(path, _productKey, _connectionAddress);
        //        case SensorType.DoubleBar:
        //            return new BarSensorDouble(path, _productKey, _connectionAddress);
        //        default:
        //            throw new InvalidEnumArgumentException($"Invalid enum argument: {type}!");
        //    }
        //}

        public event EventHandler ValuesQueueOverflow;
        public void Initialize()
        {
            _dataQueue.InitializeTimer();
        }

        public void Stop()
        {
            List<CommonSensorValue> allData = new List<CommonSensorValue>();
            if (_dataQueue != null)
            {
                allData.AddRange(_dataQueue.GetAllCollectedData());
                _dataQueue.Stop();
            }

            lock (_syncRoot)
            {
                foreach (var pair in _nameToSensor)
                {
                    if (pair.Value.HasLastValue)
                    {
                        allData.Add(pair.Value.GetLastValue());
                    }
                }
            }

            lock (_syncRoot)
            {
                foreach (var pair in _nameToSensor)
                { 
                    pair.Value.Dispose();
                }
            }


            if (allData.Any())
            {
                SendData(allData);
            }
            
            _client.Dispose();
        }
        public void InitializeSystemMonitoring()
        {
            StartSystemMonitoring();
        }

        public void InitializeProcessMonitoring()
        {
            StartCurrentProcessMonitoring();
        }

        public void InitializeProcessMonitoring(string processName)
        {
            
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

        public IDoubleBarSensor Create1HrDoubleBarSensor(string path)
        {
            return CreateDoubleBarSensor(path, 3600000);
        }

        public IDoubleBarSensor Create30MinDoubleBarSensor(string path)
        {
            return CreateDoubleBarSensor(path, 1800000);
        }

        public IDoubleBarSensor Create10MinDoubleBarSensor(string path)
        {
            return CreateDoubleBarSensor(path, 600000);
        }

        public IDoubleBarSensor Create5MinDoubleBarSensor(string path)
        {
            return CreateDoubleBarSensor(path, 300000);
        }

        public IDoubleBarSensor Create1MinDoubleBarSensor(string path)
        {
            return CreateDoubleBarSensor(path, 60000);
        }
        public IDoubleBarSensor CreateDoubleBarSensor(string path, int timeout = 30000, int smallPeriod = 15000)
        {
            var existingSensor = GetExistingSensor(path);
            var doubleBarSensor = existingSensor as IDoubleBarSensor;
            if (doubleBarSensor != null)
            {
                (doubleBarSensor as BarSensorBase)?.Restart(timeout, smallPeriod);
                return doubleBarSensor;
            }

            BarSensorDouble sensor = new BarSensorDouble(path, _productKey, _dataQueue as IValuesQueue, timeout, smallPeriod);
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

        private void StartSystemMonitoring()
        {
            CPUSensor cpuSensor = new CPUSensor(_productKey, _dataQueue as IValuesQueue);
            AddNewSensor(cpuSensor, cpuSensor.Path);
            FreeMemorySensor freeMemorySensor = new FreeMemorySensor(_productKey, _dataQueue as IValuesQueue);
            AddNewSensor(freeMemorySensor, freeMemorySensor.Path);
            //FreeDiskSpaceSensor freeDiskSpaceSensor = new FreeDiskSpaceSensor();
        }

        private void StartCurrentProcessMonitoring()
        {
            Process currentProcess = Process.GetCurrentProcess();
            ProcessCPUSensor currentCpuSensor = new ProcessCPUSensor(_productKey, _dataQueue as IValuesQueue, currentProcess.ProcessName);
            AddNewSensor(currentCpuSensor, currentCpuSensor.Path);
            ProcessMemorySensor currentMemorySensor = new ProcessMemorySensor(_productKey, _dataQueue as IValuesQueue, currentProcess.ProcessName);
            AddNewSensor(currentMemorySensor, currentMemorySensor.Path);
            ProcessThreadCountSensor currentThreadCount = new ProcessThreadCountSensor(_productKey, _dataQueue as IValuesQueue, currentProcess.ProcessName);
            AddNewSensor(currentThreadCount, currentThreadCount.Path);
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
            lock (_syncRoot)
            {
                if (_nameToSensor.ContainsKey(path))
                {
                    sensor = _nameToSensor[path] as SensorBase;
                }
            }

            if (sensor == null && sensor as IPerformanceSensor != null)
            {
                throw new InvalidSensorPathException($"Path {path} is used by standard performance sensor!");
            }
            return sensor;
        }

        private void AddNewSensor(ISensor sensor, string path)
        {
            lock (_syncRoot)
            {
                _nameToSensor[path] = sensor;
                //_countSensor.AddValue(_nameToSensor.Count);
            }
        }
        private int GetCount()
        {
            int count = 0;
            lock (_syncRoot)
            {
                count = _nameToSensor.Count;
            }

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
                //string jsonString = Serializer.Serialize(values);
                //byte[] bytesData = Encoding.UTF8.GetBytes(jsonString);
                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var res = _client.PostAsync(_listSendingAddress, data).Result;
                //HttpWebRequest request = (HttpWebRequest) WebRequest.Create(_listSendingAddress);
                //request.Method = "POST";
                //request.ContentType = "application/json";
                //using (var stream = request.GetRequestStream())
                //{
                //    stream.Write(bytesData, 0, bytesData.Length);
                //}

                //request.GetResponse();
            }
            catch (System.Exception e)
            {
                _dataQueue.ReturnFailedData(values);
                Console.WriteLine($"Failed to send: {e}");
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
