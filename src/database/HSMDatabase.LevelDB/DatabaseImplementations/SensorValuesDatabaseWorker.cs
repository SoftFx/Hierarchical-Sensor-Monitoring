using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    internal sealed class SensorValuesDatabaseWorker : ISensorValuesDatabase
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly LevelDBDatabaseAdapter _openedDb;


        public long From { get; }

        public long To { get; }


        public SensorValuesDatabaseWorker(string name, long from, long to)
        {
            _openedDb = new LevelDBDatabaseAdapter(name);

            From = from;
            To = to;
        }


        //public void OpenDatabase(string dbPath)
        //{
        //    var sensorId = Path.GetFileName(dbPath);
        //    if (!IsDatabaseExists(sensorId))
        //        _openedDb.Add(sensorId, new LevelDBDatabaseAdapter(dbPath));
        //}

        public void Dispose() => _openedDb.Dispose();

        //public void DisposeDatabase(string sensorId)
        //{
        //    try
        //    {
        //        _openedDb[sensorId].Dispose();
        //    }
        //    catch (Exception e)
        //    {
        //        _logger.Error(e, $"Failed to dispose databases for {sensorId} ({From}_{To} db)");
        //    }
        //}

        //public void RemoveDatabase(string sensorId) => _openedDb.Remove(sensorId);

        public void FillLatestValues(Dictionary<byte[], (Guid sensorId, byte[] latestValue)> keyValuePairs)
        {
            try
            {
                _openedDb.FillLatestValues(keyValuePairs);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to fill sensors latest values");
            }
        }

        public void PutSensorValue(SensorValueEntity entity)
        {
            var key = Encoding.UTF8.GetBytes(PrefixConstants.GetSensorValueKey(entity.SensorId, entity.ReceivingTime));

            try
            {
                var value = JsonSerializer.SerializeToUtf8Bytes(entity.Value);
                _openedDb.Put(key, value);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write data for {entity.SensorId}");
            }
        }

        public void PutSensorValue(string sensorId, string time, byte[] value)
        {
            var key = Encoding.UTF8.GetBytes($"{sensorId}_{time}");

            try
            {
                _openedDb.Put(key, value);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write data for {sensorId}");
            }
        }

        public void RemoveSensorValues(string sensorId)
        {
            var key = Encoding.UTF8.GetBytes(sensorId);

            try
            {
                _openedDb.DeleteAllStartingWith(key);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to remove values for sensor {sensorId}");
            }
        }

        //public bool IsDatabaseExists(string sensorId) => _openedDb.ContainsKey(sensorId);

        //public byte[] GetLatestValue(string sensorId) => _openedDb.GetLatestValue();

        public List<byte[]> GetValues(string sensorId, byte[] to, int count) =>
            _openedDb.GetStartingWithTo(to, Encoding.UTF8.GetBytes(sensorId), count);

        public List<byte[]> GetValues(string sensorId, byte[] from, byte[] to)
        {
            var result = _openedDb.GetStartingWithRange(from, to, Encoding.UTF8.GetBytes(sensorId));
            result.Reverse();

            return result;
        }
    }
}
