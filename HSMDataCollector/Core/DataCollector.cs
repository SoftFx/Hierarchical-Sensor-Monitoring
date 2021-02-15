using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using HSMDataCollector.Bar;
using HSMDataCollector.Base;
using HSMDataCollector.InstantValue;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.Core
{
    public class DataCollector : IDataCollector
    {
        private readonly string _productKey;
        private readonly string _connectionAddress;
        private readonly Dictionary<string, SensorBase> _nameToSensor;
        private readonly object _syncRoot = new object();
        private readonly HttpClient _client;
        //private readonly InstantValueSensorInt _countSensor;
        public DataCollector(string productKey, string address, int port)
        {
            _connectionAddress = $"{address}:{port}/api/sensors";
            _productKey = productKey;
            _nameToSensor = new Dictionary<string, SensorBase>();
            _client = new HttpClient();
            ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            //_countSensor = new InstantValueSensorInt("CountSensor", productKey, address);
        }
        public void Initialize()
        {
            throw new NotImplementedException();
        }

        public void CheckConnection()
        {
            throw new NotImplementedException();
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

        public IBoolSensor CreateBoolSensor(string path)
        {
            var existingSensor = GetExistingSensor(path);
            var boolSensor = existingSensor as IBoolSensor;
            if (boolSensor != null)
            {
                return boolSensor;
            }
            
            InstantValueSensorBool sensor = new InstantValueSensorBool(path, _productKey, _connectionAddress, _client);
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

            InstantValueSensorDouble sensor = new InstantValueSensorDouble(path, _productKey, _connectionAddress, _client);
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

            InstantValueSensorInt sensor = new InstantValueSensorInt(path, _productKey, _connectionAddress, _client);
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

            BarSensorDouble sensor = new BarSensorDouble(path, _productKey, _connectionAddress, _client, timeout);
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

            BarSensorInt sensor = new BarSensorInt(path, _productKey, _connectionAddress, _client, timeout);
            AddNewSensor(sensor, path);
            return sensor;
        }

        public int GetSensorCount()
        {
            return GetCount();
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
    }
}
