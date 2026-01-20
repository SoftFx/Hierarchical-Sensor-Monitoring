using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager
{
    public interface ISensorValuesDatabase : IDisposable
    {
        string Name { get; }

        long From { get; }

        long To { get; }

        bool Contains(long time);

        bool Overlaps(long from, long to);

        void FillLatestValues(Dictionary<byte[], (long from, byte[] toKey, byte[] latestValue)> keyValuePairs);

        void PutSensorValue(byte[] key, byte[] value);

        void RemoveSensorValues(byte[] from, byte[] to);

        byte[] Get(byte[] key, byte[] sensorId);

        byte[] GetLatest(byte[] to, byte[] sensorId);

        IEnumerable<byte[]> GetValuesFrom(byte[] from, byte[] to);

        IEnumerable<byte[]> GetValuesTo(byte[] from, byte[] to);

        IEnumerable<(byte[] key, byte[] value)> GetKeysValuesTo(byte[] from, byte[] to);

        void Compact();

        Dictionary<Guid, (byte[], byte[])> GetLastAndFirstValues(IEnumerable<Guid> sensorIds, Dictionary<Guid, (byte[] lastValue, byte[] firstValue)> results = null);

        IEnumerable<(byte[] key, byte[] value)> GetAll();
    }
}
