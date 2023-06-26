using System;

namespace HSMDatabase.AccessManager.DatabaseEntities;

public sealed class JournalEntity
{
    public string Value { get; set; }
}

public readonly struct Key
{
    private const int GuidSize = 16;


    public Guid Id { get; init; }

    public long Time { get; init; }

    
    public Key(Guid guid, long time)
    {
        Id = guid;
        Time = time;
    }

    public byte[] GetBytes()
    {
        
        Span<byte> result = stackalloc byte[GuidSize + sizeof(long)];

        return Id.TryWriteBytes(result) && BitConverter.TryWriteBytes(result[16..], Time) ? result.ToArray() : Array.Empty<byte>();
    }
    
    public static Key FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length != 24)
            return default;

        var id = new Guid(new ReadOnlySpan<byte>(bytes[..16]));
        var time = BitConverter.ToInt64(bytes, 16);
        return new Key(id, time);
    }
}