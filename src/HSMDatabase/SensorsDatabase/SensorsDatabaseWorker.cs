using HSMDatabase.Entity;
using HSMDatabase.LevelDB;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HSMDatabase.SensorsDatabase
{
    internal class SensorsDatabaseWorker : ISensorsDatabase
    {
        private readonly DateTime _databaseMinTime;
        private readonly DateTime _databaseMaxTime;
        private readonly string _name;
        private readonly IDatabase _database;
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
            _database = new Database(_name);
            _logger = LogManager.GetCurrentClassLogger(typeof(SensorsDatabaseWorker));
        }

        public long GetSensorSize(string productName, string path)
        {
            var stringKey = CreateKey(productName, path);
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
            var writeKey = CreateKey(productName, sensorData.Path);
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
            var stringKey = CreateKey(productName, path);
            var bytesKey = Encoding.UTF8.GetBytes(stringKey);
            try
            {
                _database.RemoveStartingWith(bytesKey);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove values for sensor {productName}/{path}");
            }
        }

        public SensorDataEntity GetLatestSensorValue(string productName, string path)
        {
            var readKey = CreateKey(productName, path);
            var bytesKey = Encoding.UTF8.GetBytes(readKey);
            var values = GetValuesWithKeyEqualOrGreater(bytesKey);
            if (values == null || !values.Any())
                return null;

            values.Sort((v1, v2) => v2.TimeCollected.CompareTo(v1.TimeCollected));
            return values.First();
        }

        public List<SensorDataEntity> GetAllSensorValues(string productName, string path)
        {
            var readKey = CreateKey(productName, path);
            var bytesKey = Encoding.UTF8.GetBytes(readKey);
            return GetValuesWithKeyEqualOrGreater(bytesKey);
        }

        //public List<SensorDataEntity> GetSensorValues(string productName, string path, int count)
        //{
        //    throw new NotImplementedException();
        //}

        public List<SensorDataEntity> GetSensorValuesFrom(string productName, string path, DateTime from)
        {
            var readKey = CreateKey(productName, path, from);
            var bytesKey = Encoding.UTF8.GetBytes(readKey);
            return GetValuesWithKeyEqualOrGreater(bytesKey);
        }

        public List<SensorDataEntity> GetSensorValuesBetween(string productName, string path, DateTime from, DateTime to)
        {
            string fromKey = CreateKey(productName, path, from);
            string toKey = CreateKey(productName, path, to);
            byte[] fromBytes = Encoding.UTF8.GetBytes(fromKey);
            byte[] toBytes = Encoding.UTF8.GetBytes(toKey);
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            try
            {
                var values = _database.GetRange(fromBytes, toBytes);
                foreach (var value in values)
                {
                    try
                    {
                        result.Add(JsonSerializer.Deserialize<SensorDataEntity>(Encoding.UTF8.GetString(value)));
                    }
                    catch (Exception e)
                    {
                        _logger.Error(e, $"Failed to deserialize {Encoding.UTF8.GetString(value)} to SensorDataEntity");
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                throw;
            }

            return result;
        }

        private List<SensorDataEntity> GetValuesWithKeyEqualOrGreater(byte[] key)
        {
            List<SensorDataEntity> result = new List<SensorDataEntity>();
            try
            {
                var values = _database.GetAllStartingWith(key);
                foreach (var value in values)
                {
                    try
                    {
                        result.Add(JsonSerializer.Deserialize<SensorDataEntity>(Encoding.UTF8.GetString(value)));
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


        #region Keys

        private string CreateKey(string productName, string path)
        {
            return $"{productName}_{path}";
        }

        private string CreateKey(string productName, string path, DateTime dateTime)
        {
            return $"{productName}_{path}_{dateTime.Ticks}";
        }
        #endregion
    }
}