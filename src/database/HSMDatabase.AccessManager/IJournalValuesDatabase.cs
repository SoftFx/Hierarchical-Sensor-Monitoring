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

    void Put(byte[] key, JournalEntity value);

    void Remove(byte[] from, byte[] to);

    byte[] Get(byte[] key, byte[] sensorId);

    IEnumerable<byte[]> GetValuesFrom(byte[] from, byte[] to);

    IEnumerable<byte[]> GetValuesTo(byte[] from, byte[] to);
}