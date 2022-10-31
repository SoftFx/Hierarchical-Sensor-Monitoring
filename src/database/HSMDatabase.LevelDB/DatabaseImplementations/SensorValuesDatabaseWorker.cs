﻿using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    internal sealed class SensorValuesDatabaseWorker : ISensorValuesDatabase
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly LevelDBDatabaseAdapter _openedDb;


        public string Name { get; }

        public long From { get; }

        public long To { get; }


        public SensorValuesDatabaseWorker(string name, long from, long to)
        {
            _openedDb = new LevelDBDatabaseAdapter(name);

            Name = name;
            From = from;
            To = to;
        }


        public void Dispose() => _openedDb.Dispose();

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

        public List<byte[]> GetValues(string sensorId, byte[] to, int count)
        {
            try
            {
                return _openedDb.GetStartingWithTo(to, Encoding.UTF8.GetBytes(sensorId), count);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed getting values for sensor {sensorId} (to: {to}, count: {count})");

                return new();
            }
        }

        public List<byte[]> GetValuesTo(string sensorId, byte[] from, byte[] to, int count)
        {
            IEnumerable<byte[]> TakeValues(List<byte[]> values, int valuesCount) =>
                values.Take(valuesCount);

            return GetValues(sensorId, from, to, count, TakeValues);
        }

        public List<byte[]> GetValuesFrom(string sensorId, byte[] from, byte[] to, int count)
        {
            IEnumerable<byte[]> TakeValues(List<byte[]> values, int valuesCount) =>
                values.TakeLast(valuesCount);

            return GetValues(sensorId, from, to, count, TakeValues);
        }

        private List<byte[]> GetValues(string sensorId, byte[] from, byte[] to, int count,
            Func<List<byte[]>, int, IEnumerable<byte[]>> takeFunc)
        {
            try
            {
                var result = _openedDb.GetStartingWithRange(from, to, Encoding.UTF8.GetBytes(sensorId));
                result.Reverse();

                return takeFunc?.Invoke(result, count).ToList();
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed getting values for sensor {sensorId} (from: {from}, to: {to}, count: {count})");

                return new();
            }
        }
    }
}
