using System;

namespace HSMDatabase.AccessManager.DatabaseEntities;

public enum RecordType : byte
{
    Changes,
    Actions
}


public sealed record JournalEntity(string Value);


public readonly struct JournalKey
{
    private const int GuidSize = 16;
    private const int StructSize = GuidSize + sizeof(RecordType) + sizeof(long);


    public Guid Id { get; }

    public long Time { get; }

    public RecordType Type { get; }


    public JournalKey(Guid id, long time, RecordType type = RecordType.Actions)
    {
        Id = id;
        Time = time;
        Type = type;
    }

    public byte[] GetBytes()
    {
        Span<byte> result = stackalloc byte[StructSize];
        result[GuidSize] = (byte)Type;

        return Id.TryWriteBytes(result) && BitConverter.TryWriteBytes(result[(GuidSize + 1)..], Time) ? result.ToArray() : Array.Empty<byte>();
    }

    public static JournalKey FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length != StructSize)
            return default;

        var id = new Guid(bytes[..GuidSize]);
        var journalType = bytes[GuidSize];
        var time = BitConverter.ToInt64(bytes, GuidSize + 1);

        return new JournalKey(id, time, (RecordType)journalType);
    }
}

