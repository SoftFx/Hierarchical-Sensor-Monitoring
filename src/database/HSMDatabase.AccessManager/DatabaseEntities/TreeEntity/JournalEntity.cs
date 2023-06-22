using System;

namespace HSMDatabase.AccessManager.DatabaseEntities;

public sealed class JournalEntity
{
    public Key Id { get; set; }
    
    public string Value { get; set; }
}

public readonly struct Key
{
    public Guid Id { get; init; }

    public long Time { get; init; }

    
    public Key(Guid guid, long time)
    {
        Id = guid;
        Time = time;
    }

    public byte[] GetBytes()
    {
        Span<byte> result = stackalloc byte[16 + sizeof(long)];
        Id.TryWriteBytes(result);
        BitConverter.TryWriteBytes(result[16..], Time);
        
        return result.ToArray();
    }
    
    public static Key FromBytes(byte[] bytes)
    {
        if (bytes == null || bytes.Length != 24)
            return default;

        var id = new Guid(new ReadOnlySpan<byte>(bytes, 0, 16));
        var time = BitConverter.ToInt64(bytes, 16);
        return new Key(id, time);
    }
}