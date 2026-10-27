using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.AccessManager;

public interface IJournalValuesDatabase : IIntervalDatabase
{
    void Put(byte[] key, JournalRecordEntity value);

    void Put(byte[] key, byte[] value);

    void Remove(byte[] from, byte[] to);

    byte[] Get(byte[] key, byte[] sensorId);

    /// <summary>Point read of one stored journal key; false when absent (#1588 record addressing).</summary>
    bool TryGet(byte[] key, out byte[] value);

    IEnumerable<(byte[] key, byte[] value)> GetValuesFrom(byte[] from, byte[] to);

    IEnumerable<(byte[] key, byte[] value)> GetValuesTo(byte[] from, byte[] to);

    void Compact();
}