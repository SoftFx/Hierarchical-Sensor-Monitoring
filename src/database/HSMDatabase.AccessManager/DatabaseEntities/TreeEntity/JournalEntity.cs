using System;
namespace HSMDatabase.AccessManager.DatabaseEntities;

public enum RecordType : byte
{
    Actions,
    Changes
}


public sealed record JournalEntity(string Value, string Initiator = "System");


public readonly struct JournalKey
{
    private const int GuidSize = 16;
    private const int TypeSize = sizeof(RecordType);
    private const int StructSize = GuidSize + TypeSize + sizeof(long);


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
        
        if (!Id.TryWriteBytes(result))
            return Array.Empty<byte>();

        if (!BitConverter.TryWriteBytes(result[(GuidSize + TypeSize)..], Time))
            return Array.Empty<byte>();
        
        result[(GuidSize + TypeSize)..].Reverse();
        return result.ToArray();
    }

    public static JournalKey FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length != StructSize)
            return default;

        var id = new Guid(bytes[..GuidSize]);
        var journalType = bytes[GuidSize];
        Array.Reverse(bytes, GuidSize + TypeSize, StructSize - (GuidSize + TypeSize));
        var time = BitConverter.ToInt64(bytes, GuidSize + TypeSize);

        return new JournalKey(id, time, (RecordType)journalType);
    }
}

