using System;
using System.Diagnostics.CodeAnalysis;

namespace HSMDatabase.AccessManager.DatabaseEntities;

[Flags]
public enum RecordType : byte
{
    Actions = 0,
    Changes = 1,
}


public sealed record JournalRecordEntity
{
    public string Enviroment { get; init; }

    public string Initiator { get; init; }


    public string PropertyName { get; init; }

    public string OldValue { get; init; }

    public string NewValue { get; init; }

    public string Path { get; init; }

    // Unified-journal tags (#1588); null on records written before the
    // unification and on records that carry no configuration meaning
    // (sensor value updates). JSON-deserialized old rows read as null.
    public string Kind { get; init; }

    public string ProductId { get; init; }

    /// <summary>The undo group (internal change-set id) the record's write landed in; null when the record is not rollbackable.</summary>
    public string ChangeSetId { get; init; }
}


public readonly struct JournalKey
{
    private const int GuidSize = 16;
    private const int GuidAndTypeSize = GuidSize + sizeof(RecordType);
    private const int StructSize = GuidAndTypeSize + sizeof(long);


    public Guid Id { get; }

    public long Time { get; }

    public RecordType Type { get; }


    private (Guid, long, RecordType) Key => (Id, Time, Type);


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

        // Read the reversed ticks WITHOUT mutating the input buffer: keys can
        // be parsed more than once on one pass (the global journal scan
        // filters by time and later maps the same pair), and an in-place
        // reverse would corrupt the second parse (#1588).
        var reversed = bytes[GuidAndTypeSize..];
        Array.Reverse(reversed);

        var time = BitConverter.ToInt64(reversed);

        return new JournalKey(id, time, (RecordType)journalType);
    }


    public override bool Equals([NotNullWhen(true)] object obj)
    {
        return obj is JournalKey key && Key == key.Key;
    }

    public override int GetHashCode() => Key.GetHashCode();
}