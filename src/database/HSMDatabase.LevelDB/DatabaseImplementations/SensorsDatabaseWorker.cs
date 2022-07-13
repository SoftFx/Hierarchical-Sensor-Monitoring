using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    internal class SensorsDatabaseWorker : ISensorsDatabase
    {
        private readonly DateTime _databaseMinTime;
        private readonly DateTime _databaseMaxTime;
        private readonly string _name;
        private readonly LevelDBDatabaseAdapter _database;
        private readonly Logger _logger;
        public long DatabaseMinTicks => _databaseMinTime.Ticks;
        public long DatabaseMaxTicks => _databaseMaxTime.Ticks;
        public DateTime DatabaseMaxDateTime => _databaseMaxTime;
        public DateTime DatabaseMinDateTime => _databaseMinTime;

        public SensorsDatabaseWorker(string name, DateTime minTime, DateTime maxTime)
        {
            _databaseMinTime = minTime;
            _databaseMaxTime = maxTime;
            _name = name;
            _database = new LevelDBDatabaseAdapter(_name);
            _logger = LogManager.GetCurrentClassLogger();
        }

        public long GetSensorSize(string productName, string path)
        {
            var stringKey = PrefixConstants.GetSensorReadValueKey(productName, path);
            var bytesKey = Encoding.UTF8.GetBytes(stringKey);
            try
            {
                return _database.GetSize(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to get size of {productName}/{path} sensor");
            }

            return 0;
        }

        public void PutSensorData(SensorDataEntity sensorData, string productName)
        {
            var writeKey = PrefixConstants.GetSensorWriteValueKey(productName, sensorData.Path, sensorData.TimeCollected);
            var bytesKey = Encoding.UTF8.GetBytes(writeKey);
            try
            {
                var serializedValue = JsonSerializer.Serialize(sensorData);
                var bytesValue = Encoding.UTF8.GetBytes(serializedValue);
                _database.Put(bytesKey, bytesValue);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write data for {productName}/{sensorData.Path}");
            }
        }

        public void DeleteAllSensorValues(string productName, string path)
        {
            var stringKey = PrefixConstants.GetSensorReadValueKey(productName, path);
            var bytesKey = Encoding.UTF8.GetBytes(stringKey);
            try
            {
                _database.DeleteAllStartingWith(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove values for sensor {productName}/{path}");
            }
        }

        public SensorDataEntity GetLatestSensorValue(string productName, string path)
        {
            var readKey = PrefixConstants.GetSensorReadValueKey(productName, path);
            var bytesKey = Encoding.UTF8.GetBytes(readKey);
            var values = GetValuesWithKeyEqualOrGreater(bytesKey, path);
            if (values == null || !values.Any())
                return null;

            values.Sort((v1, v2) => v2.TimeCollected.CompareTo(v1.TimeCollected));
            return values.First(v => v.Path == path);
        }

        public void FillLatestValues(Dictionary<byte[], (Guid sensorId, byte[] latestValue)> keyValuePairs)
        {
            try
            {
                _database.FillLatestValues(keyValuePairs);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to fill sensors latest values");
            }
        }

        public List<SensorDataEntity> GetAllSensorValues(string productName, string path)
        {
            var readKey = PrefixConstants.GetSensorReadValueKey(productName, path);
            var bytesKey = Encoding.UTF8.GetBytes(readKey);
            return GetValuesWithKeyEqualOrGreater(bytesKey, path);
        }

        public List<byte[]> GetAllSensorValuesBytes(string productName, string path)
        {
            var bytesKey = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorReadValueKey(productName, path));

            return GetValuesBytesWithKeyEqualOrGreater(bytesKey);
        }

        public List<SensorDataEntity> GetSensorValuesFrom(string productName, string path, DateTime from)
        {
            var readKey = PrefixConstants.GetSensorWriteValueKey(productName, path, from);
            byte[] bytesKey = Encoding.UTF8.GetBytes(readKey);
            var startWithKey = PrefixConstants.GetSensorReadValueKey(productName, path);
            byte[] startWithBytes = Encoding.UTF8.GetBytes(startWithKey);
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            try
            {
                var values = _database.GetAllStartingWithAndSeek(startWithBytes, bytesKey);
                foreach (var value in values)
                {
                    try
                    {
                        var currentEl = JsonSerializer.Deserialize<SensorDataEntity>(Encoding.UTF8.GetString(value));
                        if (currentEl.Path == path && currentEl.TimeCollected > from)
                            result.Add(currentEl);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, $"Failed to deserialize {Encoding.UTF8.GetString(value)} to SensorDataEntity");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read all sensors values for {Encoding.UTF8.GetString(bytesKey)}");
            }

            return result;
        }

        public List<SensorDataEntity> GetSensorValuesBetween(string productName, string path, DateTime from, DateTime to)
        {
            string fromKey = PrefixConstants.GetSensorWriteValueKey(productName, path, from);
            string toKey = PrefixConstants.GetSensorWriteValueKey(productName, path, to);
            string startWithKey = PrefixConstants.GetSensorReadValueKey(productName, path);
            byte[] fromBytes = Encoding.UTF8.GetBytes(fromKey);
            byte[] toBytes = Encoding.UTF8.GetBytes(toKey);
            byte[] startWithBytes = Encoding.UTF8.GetBytes(startWithKey);
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            try
            {
                var values = _database.GetStartingWithRange(fromBytes, toBytes, startWithBytes);
                foreach (var value in values)
                {
                    try
                    {
                        var currentEl = JsonSerializer.Deserialize<SensorDataEntity>(Encoding.UTF8.GetString(value));
                        if (currentEl.Path == path && (currentEl.TimeCollected < to && currentEl.TimeCollected > from))
                            result.Add(currentEl);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, $"Failed to deserialize {Encoding.UTF8.GetString(value)} to SensorDataEntity");
                    }
                }
            }
            catch (Exception)
            { }

            return result;
        }

        public List<byte[]> GetSensorValuesBytesBetween(string productName, string path, DateTime from, DateTime to)
        {
            var fromBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorWriteValueKey(productName, path, from));
            var toBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorWriteValueKey(productName, path, to));
            var startWithBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorReadValueKey(productName, path));

            try
            {
                var result = _database.GetStartingWithRange(fromBytes, toBytes, startWithBytes);
                result.Reverse();

                return result;
            }
            catch (Exception)
            { }

            return new();
        }

        public List<byte[]> GetSensorValues(string productName, string path, DateTime to, int count)
        {
            var toBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorWriteValueKey(productName, path, to));
            var startWithBytes = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorReadValueKey(productName, path));

            try
            {
                return _database.GetStartingWithTo(toBytes, startWithBytes, count);
            }
            catch (Exception)
            { }

            return new();
        }

        private List<SensorDataEntity> GetValuesWithKeyEqualOrGreater(byte[] key, string path)
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            try
            {
                var values = _database.GetAllStartingWith(key);
                foreach (var value in values)
                {
                    try
                    {
                        var currentEl = JsonSerializer.Deserialize<SensorDataEntity>(Encoding.UTF8.GetString(value));
                        if (currentEl.Path == path)
                            result.Add(currentEl);
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, $"Failed to deserialize {Encoding.UTF8.GetString(value)} to SensorDataEntity");
                    }
                }
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read all sensors values for {Encoding.UTF8.GetString(key)}");
            }

            return result;
        }

        private List<byte[]> GetValuesBytesWithKeyEqualOrGreater(byte[] key)
        {
            try
            {
                return _database.GetAllStartingWith(key);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to read all sensors values bytes for {Encoding.UTF8.GetString(key)}");
            }

            return new List<byte[]>();
        }

        public void Dispose() => _database.Dispose();
    }
}
