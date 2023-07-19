using System;

namespace HSMDatabase.AccessManager.DatabaseEntities;

[Flags]
public enum RecordType : byte
{
    Actions = 0,
    Changes = 1,
}


public sealed record JournalEntity
{
    public string Initiator { get; init; }

    public string OldValue { get; init; }

    public string NewValue { get; init; }

    public string Path { get; init; }


    public JournalEntity(string value, string path, string initiator)
    {
        OldValue = value;
        Path = path;
        Initiator = initiator;
    }
}


public readonly struct JournalKey
{
    private const int GuidSize = 16;
    private const int GuidAndTypeSize = GuidSize + sizeof(RecordType);
    private const int StructSize = GuidAndTypeSize + sizeof(long);


    public Guid Id { get; }

    public long Time { get; }

    public RecordType Type { get; }


    public JournalKey(Guid id, long time, RecordType type = RecordType.Changes)
    {
        Id = id;
        Time = time;
        Type = type;
    }


    public byte[] GetBytes()
    {
        Span<byte> result = stackalloc byte[StructSize];
        result[GuidSize] = (byte)Type;

        if (!Id.TryWriteBytes(result) || !BitConverter.TryWriteBytes(result[GuidAndTypeSize..], Time))
            return Array.Empty<byte>();

        result[GuidAndTypeSize..].Reverse();
        return result.ToArray();
    }

    public static JournalKey FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length != StructSize)
            return default;

        var id = new Guid(bytes[..GuidSize]);
        var journalType = bytes[GuidSize];

        Array.Reverse(bytes, GuidAndTypeSize, sizeof(long));

        var time = BitConverter.ToInt64(bytes, GuidAndTypeSize);

        return new JournalKey(id, time, (RecordType)journalType);
    }
}