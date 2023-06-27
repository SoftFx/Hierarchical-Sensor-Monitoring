using System;

namespace HSMDatabase.AccessManager.DatabaseEntities;

public sealed class JournalEntity
{
    public string Value { get; set; }
}

public readonly struct Key
{
    private const int GuidSize = 16;

    private const int StructSize = GuidSize + sizeof(long) + sizeof(UpdateType);


    public Guid Id { get; init; }

    public long Time { get; init; }

    public UpdateType UpdateType { get; init; }
    
    
    public Key(Guid id, long time)
    {
        Id = id;
        Time = time;
    }

    public Key(Guid id, long time, UpdateType updateType)
    {
        Id = id;
        Time = time;
        UpdateType = updateType;
    }

    public byte[] GetBytes()
    {
        Span<byte> result = stackalloc byte[StructSize];
        result[^1] = (byte)UpdateType;

        return Id.TryWriteBytes(result) && BitConverter.TryWriteBytes(result[16..], Time) ? result.ToArray() : Array.Empty<byte>();
    }
    
    public static Key FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length != StructSize)
            return default;

        var id = new Guid(new ReadOnlySpan<byte>(bytes[..16]));
        var time = BitConverter.ToInt64(bytes, 16);
        var updateType = bytes[^1];
        
        return new Key(id, time, (UpdateType)updateType);
    }
}

public enum UpdateType : byte
{
    Changes,
    Actions
}