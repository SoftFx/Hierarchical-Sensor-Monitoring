using System;

namespace HSMDatabase.AccessManager.DatabaseEntities;

public sealed class JournalEntity
{
    public byte[] Id { get; set; }

    public string Name { get; set; }
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
        var guidBytes = Id.ToByteArray();
        var timeBytes = BitConverter.GetBytes(Time);
        var result = new byte[guidBytes.Length + timeBytes.Length];
        
        Buffer.BlockCopy(guidBytes, 0, result, 0, guidBytes.Length);
        Buffer.BlockCopy(timeBytes, 0, result, guidBytes.Length, timeBytes.Length);
        
        return result;
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