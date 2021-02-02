using System;
using System.Collections.Generic;
using System.ComponentModel;
using HSMDataCollector.Bar;
using HSMDataCollector.Base;
using HSMDataCollector.InstantValue;

namespace HSMDataCollector.Core
{
    public class DataCollector : IDataCollector
    {
        private readonly string _productKey;
        private readonly string _connectionAddress;
        private readonly Dictionary<string, ISensor> _nameToSensor;
        private readonly object _syncRoot = new object();
        private readonly InstantValueSensorInt _countSensor;
        public DataCollector(string productKey, string address, int port)
        {
            _connectionAddress = $"{address}:{port}/api/sensors";
            _productKey = productKey;
            _nameToSensor = new Dictionary<string, ISensor>();
            _countSensor = new InstantValueSensorInt("CountSensor", productKey, address);
        }
        public void Initialize()
        {
            throw new NotImplementedException();
        }

        public void CheckConnection()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates or returns an existing sensor object with same 'name' and 'path' values.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="type"></param>
        /// <exception cref="InvalidEnumArgumentException">The exception is thrown when an unknown SensorType value is used.</exception>
        /// <returns>ISensor derived class instance.</returns>
        public ISensor CreateSensor(string path, SensorType type)
        {
            ISensor result = GetExistingSensor(path);
            if (result != null)
                return result;

            result = CreateNewSensor(path, type);
            AddNewSensor(result, path);
            return result;
        }

        private ISensor CreateNewSensor(string path, SensorType type)
        {
            switch (type)
            {
                case SensorType.BooleanValue:
                    return new InstantValueSensorBool(path, _productKey, _connectionAddress);
                case SensorType.IntValue:
                    return new InstantValueSensorInt(path, _productKey, _connectionAddress);
                case SensorType.DoubleValue:
                    return new InstantValueSensorDouble(path, _productKey, _connectionAddress);
                case SensorType.StringValue:
                    return new InstantValueSensorString(path, _productKey, _connectionAddress);
                case SensorType.IntegerBar:
                    return new BarSensorInt(path, _productKey, _connectionAddress);
                case SensorType.DoubleBar:
                    return new BarSensorDouble(path, _productKey, _connectionAddress);
                default:
                    throw new InvalidEnumArgumentException($"Invalid enum argument: {type}!");
            }
        }

        private ISensor GetExistingSensor(string path)
        {
            ISensor sensor = null;
            lock (_syncRoot)
            {
                if (_nameToSensor.ContainsKey(path))
                {
                    sensor = _nameToSensor[path];
                }
            }

            return sensor;
        }

        private void AddNewSensor(ISensor sensor, string path)
        {
            lock (_syncRoot)
            {
                _nameToSensor[path] = sensor;
                _countSensor.AddValue(_nameToSensor.Count);
            }
        }
    }
}
