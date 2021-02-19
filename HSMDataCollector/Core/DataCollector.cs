using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using HSMDataCollector.Bar;
using HSMDataCollector.Base;
using HSMDataCollector.InstantValue;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;

namespace HSMDataCollector.Core
{
    public class DataCollector : IDataCollector
    {
        private readonly string _productKey;
        private readonly string _connectionAddress;
        private readonly string _listSendingAddress;
        private readonly Dictionary<string, SensorBase> _nameToSensor;
        private readonly object _syncRoot = new object();
        private readonly HttpClient _client;
        private readonly IDataQueue _dataQueue;
        //private readonly InstantValueSensorInt _countSensor;
        public DataCollector(string productKey, string address, int port)
        {
            _connectionAddress = $"{address}:{port}/api/sensors";
            _listSendingAddress = $"{_connectionAddress}/list";
            _productKey = productKey;
            _nameToSensor = new Dictionary<string, SensorBase>();
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
        public IBoolSensor CreateBoolSensor(string path)
        {
            var existingSensor = GetExistingSensor(path);
            var boolSensor = existingSensor as IBoolSensor;
            if (boolSensor != null)
            {
                return boolSensor;
            }
            
            InstantValueSensorBool sensor = new InstantValueSensorBool(path, _productKey, _connectionAddress, _dataQueue as IValuesQueue);
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

            InstantValueSensorDouble sensor = new InstantValueSensorDouble(path, _productKey, _connectionAddress, _dataQueue as IValuesQueue);
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

            InstantValueSensorInt sensor = new InstantValueSensorInt(path, _productKey, _connectionAddress, _dataQueue as IValuesQueue);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public IDoubleBarSensor CreateDoubleBarSensor(string path, int timeout = 30000)
        {
            var existingSensor = GetExistingSensor(path);
            var doubleBarSensor = existingSensor as IDoubleBarSensor;
            if (doubleBarSensor != null)
            {
                return doubleBarSensor;
            }

            BarSensorDouble sensor = new BarSensorDouble(path, _productKey, _connectionAddress, _dataQueue as IValuesQueue, timeout);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public IIntBarSensor CreateIntBarSensor(string path, int timeout = 30000)
        {
            var existingSensor = GetExistingSensor(path);
            var intBarSensor = existingSensor as IIntBarSensor;
            if (intBarSensor != null)
            {
                return intBarSensor;
            }

            BarSensorInt sensor = new BarSensorInt(path, _productKey, _connectionAddress, _dataQueue as IValuesQueue, timeout);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public int GetSensorCount()
        {
            return GetCount();
        }

        private SensorBase GetExistingSensor(string path)
        {
            SensorBase sensor = null;
            lock (_syncRoot)
            {
                if (_nameToSensor.ContainsKey(path))
                {
                    sensor = _nameToSensor[path];
                }
            }

            return sensor;
        }

        private void AddNewSensor(SensorBase sensor, string path)
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
                //Console.WriteLine($"Sending {values.Count} values at {DateTime.Now:G}");
                string jsonString = JsonSerializer.Serialize(values);
                var data = new StringContent(jsonString, Encoding.UTF8, "application/json");
                _client.PostAsync(_listSendingAddress, data);
            }
            catch (Exception e)
            {
                _dataQueue.ReturnFailedData(values);
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
