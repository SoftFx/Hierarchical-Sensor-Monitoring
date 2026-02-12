using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using HSMDatabase.AccessManager;
using HSMDatabase.LevelDB.Extensions;


namespace HSMDatabase.LevelDB.DatabaseImplementations
{
    internal sealed class SensorValuesDatabaseWorker : IntervalDataseBase, ISensorValuesDatabase
    {

        public SensorValuesDatabaseWorker(string name, long from, long to) : base(name, from, to)
        {
        }


        public void FillLatestValues(Dictionary<byte[], (long, byte[], byte[])> keyValuePairs)
        {
            try
            {
                _openedDb.FillLatestValues(keyValuePairs, To);
            }
            catch (Exception e)
            {
                _logger.Error(e, "Failed to fill sensors latest values");
            }
        }

        public void PutSensorValue(byte[] key, byte[] value)
        {
            try
            {
                _openedDb.Put(key, value);
            }
            catch (Exception e)
            {
                _logger.Error(e, $"Failed to write data for {key.GetString()}");
            }
        }

        public void RemoveSensorValues(byte[] from, byte[] to)
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

                return [];
            }
        }

        public byte[] GetLatest(byte[] key, byte[] sensorId)
        {
            try
            {
                return _openedDb.GetLatest(key, sensorId);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting latest value {key.GetString()} - {e.Message}");

                return [];
            }
        }

        public byte[] GetFirst(byte[] key, byte[] sensorId)
        {
            try
            {
                return _openedDb.GetFirst(key, sensorId);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting first value {key.GetString()} - {e.Message}");

                return null;
            }
        }

        public Dictionary<Guid, (byte[], byte[])> GetLastAndFirstValues(IEnumerable<Guid> sensorIds,Dictionary<Guid, (byte[] lastValue, byte[] firstValue)> results = null)
        {
            try
            {
                return _openedDb.GetLastAndFirstValues(sensorIds, results);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting latest values - {e.Message}");
                return new Dictionary<Guid, (byte[], byte[])>();
            }
        }

        public IEnumerable<byte[]> GetValuesFrom(byte[] from, byte[] to)
        {
            try
            {
                return _openedDb.GetValueFromTo(from, to);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting value [{from.GetString()}, {to.GetString()}] - {e.Message}");

                return Enumerable.Empty<byte[]>();
            }
        }

        public IEnumerable<byte[]> GetValuesTo(byte[] from, byte[] to)
        {
            try
            {
                return _openedDb.GetValueToFrom(from, to);
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting value [{to.GetString()}, {from.GetString()}] - {e.Message}");

                return Enumerable.Empty<byte[]>();
            }
        }

        public IEnumerable<(byte[] key, byte[] value)> GetKeysValuesTo(byte[] from, byte[] to)
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

        public IEnumerable<(byte[] key, byte[] value)> GetAll()
        {
            try
            {
                return _openedDb.GetAll();
            }
            catch (Exception e)
            {
                _logger.Error($"Failed getting all values - {e.Message}");

                return [];
            }
        }

        public void Compact()
        {
            _openedDb.Compact();
        }

    }
}