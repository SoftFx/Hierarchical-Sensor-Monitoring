using HSMDatabase.AccessManager;
using NLog;
using System;
using System.Collections.Generic;
using System.Text;

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

        public void Dispose() => _database.Dispose();
    }
}
