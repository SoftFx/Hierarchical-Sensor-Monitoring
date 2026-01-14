using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB.Extensions;
using NLog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    internal sealed class JournalValuesDatabaseWorker : IJournalValuesDatabase
    {
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();
        private readonly LevelDBDatabaseAdapter _openedDb;


        public string Name { get; }

        public long From { get; }

        public long To { get; }


        public JournalValuesDatabaseWorker(string name, long from, long to)
        {
            _openedDb = new LevelDBDatabaseAdapter(name);

            Name = name;
            From = from;
            To = to;
        }


        public void Dispose() => _openedDb.Dispose();


        public bool IsInclude(long time) => From <= time && time < To;

        public bool IsInclude(long from, long to) => From <= to && To >= from;
        

        public void Put(byte[] key, JournalRecordEntity value)
        {
            try
            {
                var valueBytes = JsonSerializer.SerializeToUtf8Bytes(value);
                _openedDb.Put(key, valueBytes);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write data for {JournalKey.FromBytes(key).Id}");
            }
        }
        
        public void Put(byte[] key, byte[] value)
        {
            try
            {
                _openedDb.Put(key, value);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write data for {JournalKey.FromBytes(key).Id}");
            }
        }

        public void Remove(byte[] from, byte[] to)
        {
            try
            {
                _openedDb.DeleteValueFromTo(from, to);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed removing values [{from.GetString()}, {to.GetString()}] - {e.Message}");
            }
        }

        public byte[] Get(byte[] key, byte[] sensorId)
        {
            try
            {
                return _openedDb.Get(key, sensorId);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting value {key.GetString()} - {e.Message}");

                return Array.Empty<byte>();
            }
        }

        public IEnumerable<(byte[] key, byte[] value)> GetValuesFrom(byte[] from, byte[] to)
        {
            try
            {
                return _openedDb.GetValueKeyPairFromTo(from, to);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting value [{from.GetString()}, {to.GetString()}] - {e.Message}");

                return Enumerable.Empty<(byte[], byte[])>();
            }
        }

        public IEnumerable<(byte[] key, byte[] value)> GetValuesTo(byte[] from, byte[] to)
        {
            try
            {
                return _openedDb.GetValueKeyPairToFrom(from, to);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting value [{to.GetString()}, {from.GetString()}] - {e.Message}");

                return Enumerable.Empty<(byte[], byte[])>();
            }
        }

        public void Compact()
        {
            _openedDb.Compact();
        }

    }
}
