using System;
using System.Collections.Generic;
using System.Threading;

namespace HSMDatabase.AccessManager
{
    public interface ISensorValuesDatabase : IIntervalDatabase
    {
        void FillLatestValues(Dictionary<byte[], (long from, byte[] toKey, byte[] latestValue)> keyValuePairs);

        void PutSensorValue(byte[] key, byte[] value);

        /// <summary>Like <see cref="PutSensorValue"/>, but a failed write throws instead of being logged and swallowed.</summary>
        void PutSensorValueOrThrow(byte[] key, byte[] value);

        void RemoveSensorValues(byte[] from, byte[] to);

        /// <summary>
        /// Every put and every delete since the week was opened. The sealing job watches it to know when to count a
        /// sealed week's foldable rows again (#1804); the compaction bookkeeping does not use it (see
        /// <see cref="IIntervalDatabase.ReclaimableChangeCount"/>).
        /// </summary>
        long WriteCount { get; }

        byte[] Get(byte[] key, byte[] sensorId);

        byte[] GetLatest(byte[] to, byte[] sensorId);

        byte[] GetFirst(byte[] to, byte[] sensorId);

        IEnumerable<byte[]> GetValuesFrom(byte[] from, byte[] to);

        IEnumerable<byte[]> GetValuesTo(byte[] from, byte[] to);

        IEnumerable<(byte[] key, byte[] value)> GetKeysValuesTo(byte[] from, byte[] to);

        /// <summary>
        /// The values in [<paramref name="from"/>, <paramref name="to"/>] whose stored bytes start with
        /// <paramref name="firstByte"/>, oldest first (#1913: the large values of a File sensor, by their union tag). The
        /// other values are passed by without being copied out of the database. Unlike the other reads, a read that fails
        /// throws, from the call or from the enumeration; <paramref name="token"/> is asked at every row.
        /// </summary>
        IEnumerable<byte[]> GetValuesWithFirstByte(byte[] from, byte[] to, byte firstByte, CancellationToken token = default);

        Dictionary<Guid, (byte[], byte[])> GetLastAndFirstValues(IEnumerable<Guid> sensorIds, Dictionary<Guid, (byte[] lastValue, byte[] firstValue)> results = null);

        IEnumerable<(byte[] key, byte[] value)> GetAll();

        /// <summary>
        /// The history of the key range [<paramref name="from"/>, <paramref name="to"/>] as stored: the number of
        /// values a read returns, and the bytes of the keys and values that hold them on disk. For a row database
        /// that is the rows; a sealed week adds its chunks (key and chunk bytes) to its overlay rows.
        /// </summary>
        (long count, long keyBytes, long valueBytes) CalculateHistorySize(byte[] from, byte[] to);
    }
}
