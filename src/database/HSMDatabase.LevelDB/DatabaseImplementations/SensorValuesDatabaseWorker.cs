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
        private readonly Dictionary<string, LevelDBDatabaseAdapter> _openedDbs = new();


        public long From { get; }

        public long To { get; }


        public SensorValuesDatabaseWorker(long from, long to)
        {
            From = from;
            To = to;
        }


        public void OpenDatabase(string dbPath)
        {
            var dbKey = Path.GetFileName(dbPath);
            if (!_openedDbs.ContainsKey(dbKey))
                _openedDbs.Add(dbKey, new LevelDBDatabaseAdapter(dbPath));
        }

        public void PutSensorValue(SensorValueEntity entity)
        {
            var key = Encoding.UTF8.GetBytes(entity.ReceivingTime.ToString());

            try
            {
                var value = JsonSerializer.SerializeToUtf8Bytes(entity.Value);
                _openedDbs[entity.SensorId].Put(key, value);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write data for {entity.SensorId}");
            }
        }

        public void DisposeDatabase(string sensorId)
        {
            try
            {
                _openedDbs[sensorId].Dispose();
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to dispose databases for {sensorId} ({From}_{To} db)");
            }
        }
    }
}
