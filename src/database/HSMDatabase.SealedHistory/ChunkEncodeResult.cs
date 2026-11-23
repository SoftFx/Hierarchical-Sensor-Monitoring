using System;
using System.Collections.Generic;

namespace HSMDatabase.SealedHistory
{
    /// <summary>
    /// Why <see cref="ChunkCodec.Encode(IReadOnlyList{HistoryRow})"/> refused its input. A refusal covers the whole
    /// input: no chunk is produced and the caller keeps the rows as they are.
    /// </summary>
    public enum ChunkRefusalReason
    {
        /// <summary>The input holds no rows.</summary>
        EmptyInput,
        /// <summary>A row has a null value.</summary>
        NullValue,
        /// <summary>The value type is not sealed (File) or the runtime type is not one of the persisted value records.</summary>
        UnsupportedValueType,
        /// <summary>The rows do not all have the same value type.</summary>
        MixedValueTypes,
        /// <summary>The keys are not strictly ascending.</summary>
        KeysNotAscending,
        /// <summary><c>Time + round((ReceivingTime - Time) / 1 ms) * 1 ms</c> falls outside the <see cref="DateTime"/> range.</summary>
        ReceivingTimeOutOfRange,
        /// <summary>A string is not valid UTF-16 (a lone surrogate) and cannot be stored as UTF-8 exactly.</summary>
        InvalidString,
        /// <summary>A field holds a value the format cannot reproduce exactly (see the message).</summary>
        UnrepresentableValue,
        /// <summary>The encoded payload of one chunk would exceed <see cref="ChunkCodec.MaxPayloadBytes"/>.</summary>
        ChunkTooLarge,
    }


    public sealed class ChunkRefusal
    {
        public ChunkRefusalReason Reason { get; }

        /// <summary>Index of the first offending row in the input, or -1 when the refusal is not about one row.</summary>
        public int RowIndex { get; }

        public string Message { get; }


        internal ChunkRefusal(ChunkRefusalReason reason, int rowIndex, string message)
        {
            Reason = reason;
            RowIndex = rowIndex;
            Message = message;
        }


        public override string ToString() => RowIndex >= 0 ? $"{Reason} at row {RowIndex}: {Message}" : $"{Reason}: {Message}";
    }


    /// <summary>
    /// Either the chunks of the whole input, in key order, or the refusal. Never both, never a partial set.
    /// </summary>
    public sealed class ChunkEncodeResult
    {
        public IReadOnlyList<byte[]> Chunks { get; }

        public ChunkRefusal Refusal { get; }

        public bool IsSuccess => Refusal is null;


        private ChunkEncodeResult(IReadOnlyList<byte[]> chunks, ChunkRefusal refusal)
        {
            Chunks = chunks;
            Refusal = refusal;
        }


        internal static ChunkEncodeResult Success(IReadOnlyList<byte[]> chunks) => new(chunks, null);

        internal static ChunkEncodeResult Refuse(ChunkRefusalReason reason, int rowIndex, string message) =>
            new(Array.Empty<byte[]>(), new ChunkRefusal(reason, rowIndex, message));


        public override string ToString() => IsSuccess ? $"{Chunks.Count} chunk(s)" : $"Refused: {Refusal}";
    }
}
