using System;
using System.IO;

namespace HSMDatabase.SealedHistory
{
    public enum ChunkFormatError
    {
        /// <summary>The bytes do not start with the chunk magic: this is not a chunk.</summary>
        NotAChunk,
        /// <summary>The chunk was written in a format version this build cannot read.</summary>
        UnknownVersion,
        /// <summary>The chunk is shorter than its header or its declared content.</summary>
        Truncated,
        /// <summary>The checksum over the header and the stored payload does not match.</summary>
        ChecksumMismatch,
        /// <summary>The checksum matched but the content is structurally invalid.</summary>
        Corrupt,
    }


    /// <summary>
    /// Thrown when a chunk cannot be read. A chunk that fails to read never yields an empty or a partial
    /// list of rows: the caller gets this exception instead.
    /// </summary>
    public sealed class ChunkFormatException : IOException
    {
        public ChunkFormatError Error { get; }


        public ChunkFormatException(ChunkFormatError error, string message, Exception inner = null)
            : base($"Sealed history chunk: {error}: {message}", inner)
        {
            Error = error;
        }
    }
}
