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
        private readonly Dictionary<(string, string), ISensor> _nameToSensor;
        private readonly object _syncRoot = new object();
        public DataCollector(string productKey, string address, int port)
        {
            _connectionAddress = $"{address}:{port}";
            _productKey = productKey;
            _nameToSensor = new Dictionary<(string, string), ISensor>();
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
        /// <param name="name"></param>
        /// <param name="path"></param>
        /// <param name="type"></param>
        /// <exception cref="InvalidEnumArgumentException">The exception is thrown when an unknown SensorType value is used.</exception>
        /// <returns>ISensor derived class instance.</returns>
        public ISensor CreateSensor(string name, string path, SensorType type)
        {
            ISensor result = GetExistingSensor(name, path);
            if (result != null)
                return result;

            result = CreateNewSensor(name, path, type);
            AddNewSensor(result, name, path);
            return result;
        }

        private ISensor CreateNewSensor(string name, string path, SensorType type)
        {
            switch (type)
            {
                case SensorType.BooleanValue:
                    return new InstantValueSensorBool(name, path, _productKey, _connectionAddress);
                case SensorType.IntValue:
                    return new InstantValueSensorInt(name, path, _productKey, _connectionAddress);
                case SensorType.DoubleValue:
                    return new InstantValueSensorDouble(name, path, _productKey, _connectionAddress);
                case SensorType.StringValue:
                    return new InstantValueSensorString(name, path, _productKey, _connectionAddress);
                case SensorType.IntegerBar:
                    return new BarSensorInt(name, path, _productKey, _connectionAddress);
                case SensorType.DoubleBar:
                    return new BarSensorDouble(name, path, _productKey, _connectionAddress);
                default:
                    throw new InvalidEnumArgumentException($"Invalid enum argument: {type}!");
            }
        }

        private ISensor GetExistingSensor(string name, string path)
        {
            ISensor sensor = null;
            lock (_syncRoot)
            {
                if (_nameToSensor.ContainsKey((name, path)))
                {
                    sensor = _nameToSensor[(name, path)];
                }
            }

            return sensor;
        }

        private void AddNewSensor(ISensor sensor, string name, string path)
        {
            lock (_syncRoot)
            {
                _nameToSensor[(name, path)] = sensor;
            }
        }
    }
}
