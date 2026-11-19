using HSMCommon.Model;

namespace HSMDatabase.SealedHistory
{
    /// <summary>
    /// One history row of a sensor: the tick part of its storage key (<c>sensorId | tick</c>) and its value.
    /// </summary>
    public readonly record struct HistoryRow(long Key, BaseValue Value);


    /// <summary>
    /// The fixed header of a chunk, readable without decompressing or decoding the payload.
    /// <see cref="EncoderRevision"/> names the encoder that wrote the chunk; it is informational and never
    /// changes how the chunk is decoded (see <see cref="ChunkCodec.EncoderRevision"/>).
    /// </summary>
    public readonly record struct ChunkHeader(byte FormatVersion, SensorType ValueType, int RowCount, long FirstKey, long LastKey, byte EncoderRevision);
}
