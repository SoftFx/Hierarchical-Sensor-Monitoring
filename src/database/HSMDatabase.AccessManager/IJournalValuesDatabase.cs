using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.AccessManager;

public interface IJournalValuesDatabase : IDisposable
{
    string Name { get; }

    long From { get; }

    long To { get; }


    bool IsInclude(long time);

    bool IsInclude(long from, long to);

    void Put(byte[] key, JournalRecordEntity value);

    void Put(byte[] key, byte[] value);

    void Remove(byte[] from, byte[] to);

    byte[] Get(byte[] key, byte[] sensorId);

    IEnumerable<(byte[] key, byte[] value)> GetValuesFrom(byte[] from, byte[] to);

    IEnumerable<(byte[] key, byte[] value)> GetValuesTo(byte[] from, byte[] to);

    void Compact();
}