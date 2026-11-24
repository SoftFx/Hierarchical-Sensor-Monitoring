using System;
using System.IO;

namespace HSMTableBlocks
{
    /// <summary>Why a table block could not be read.</summary>
    public enum TableBlockError
    {
        /// <summary>The bytes do not start with the block magic: this is not a table block.</summary>
        BadMagic = 1,

        /// <summary>The block was written in a format version this build cannot read.</summary>
        UnknownVersion = 2,

        /// <summary>The block is shorter than its header or than the compressed length the header declares.</summary>
        Truncated = 3,

        /// <summary>
        /// Lengths do not add up: bytes after the declared payload, a payload that decompresses to another length than
        /// the header declares, or bytes left after the last column.
        /// </summary>
        LengthMismatch = 4,

        /// <summary>The CRC-32 over the header fields and the compressed payload does not match.</summary>
        ChecksumMismatch = 5,

        /// <summary>The block exceeds one of the reader's <see cref="TableBlockLimits"/>.</summary>
        LimitExceeded = 6,

        /// <summary>A header field is out of its range: reserved bytes not zero, a negative length, no tables, fewer rows than tables.</summary>
        CorruptHeader = 7,

        /// <summary>The payload is not exactly one valid zstd frame.</summary>
        CorruptFrame = 8,

        /// <summary>A directory entry is invalid: an empty table identifier, no rows, no columns, an unknown column type, a first column that is not time.</summary>
        CorruptDirectory = 9,

        /// <summary>The directory describes more bytes than the payload holds.</summary>
        DirectoryOutOfBounds = 10,

        /// <summary>Two directory entries carry the same table identifier.</summary>
        DuplicateTable = 11,

        /// <summary>A time value is outside <c>0..DateTime.MaxValue.Ticks</c>.</summary>
        InvalidTime = 12,

        /// <summary>The header disagrees with the content: the row count, or the first and last time.</summary>
        HeaderMismatch = 13,
    }


    /// <summary>
    /// Thrown when a table block cannot be read. A block that fails to read never yields a partial result: the caller
    /// gets this exception instead, with the reason in <see cref="Error"/>.
    /// </summary>
    public sealed class TableBlockFormatException : IOException
    {
        /// <summary>The reason the block was refused.</summary>
        public TableBlockError Error { get; }


        /// <summary>Creates the exception for <paramref name="error"/>.</summary>
        public TableBlockFormatException(TableBlockError error, string message, Exception inner = null)
            : base($"Table block: {error}: {message}", inner)
        {
            Error = error;
        }
    }
}
