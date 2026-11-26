using System;
using System.Buffers.Binary;

namespace HSMServer.SensorApi.Sessions
{
    internal enum TableFrameKind : byte
    {
        Block = 1,

        // A header without a block, answered with an ordinary ack: lets a collector see at once
        // that binary frames reach the server through whatever sits in between.
        Probe = 2,
    }


    // The header of a binary frame on a table connection (#1845), version 1, little-endian:
    //
    //   0   1  frame version, 1
    //   1   1  kind: 1 block, 2 probe
    //   2   2  reserved, 0
    //   4   4  header length (u32): the offset of the block; 40 in version 1, more in a later one
    //   8   8  request id (i64)
    //  16  16  producer id: 16 opaque bytes, read as a Guid and stored as they are
    //  32   8  producer sequence (i64)
    //  40   …  the block, to the end of the frame (none for a probe)
    //
    // Documented in aicontext/features/api/wire-contract/feature.md, which is the contract.
    internal readonly struct TableFrame
    {
        public const byte Version = 1;
        public const int HeaderLength = 40;

        public TableFrameKind Kind { get; init; }

        public long Id { get; init; }

        public Guid ProducerId { get; init; }

        public long ProducerSequence { get; init; }

        // Where the block starts in the frame.
        public int BlockOffset { get; init; }


        // False with the reason when the header cannot be trusted: there is then no request id to
        // answer, and the caller closes the connection (1002).
        public static bool TryRead(ReadOnlySpan<byte> frame, out TableFrame header, out string error)
        {
            header = default;

            if (frame.Length < HeaderLength)
            {
                error = $"a binary frame of {frame.Length} bytes is shorter than its {HeaderLength}-byte header";
                return false;
            }

            if (frame[0] != Version)
            {
                error = $"binary frame version {frame[0]} is not supported";
                return false;
            }

            var kind = (TableFrameKind)frame[1];

            if (kind is not (TableFrameKind.Block or TableFrameKind.Probe))
            {
                error = $"binary frame kind {frame[1]} is not supported";
                return false;
            }

            if (frame[2] != 0 || frame[3] != 0)
            {
                error = "the reserved bytes of a binary frame are not zero";
                return false;
            }

            var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(frame[4..]);

            if (headerLength < HeaderLength || headerLength > (uint)frame.Length)
            {
                error = $"the header length {headerLength} of a binary frame is outside {HeaderLength}..{frame.Length}";
                return false;
            }

            header = new TableFrame
            {
                Kind = kind,
                Id = BinaryPrimitives.ReadInt64LittleEndian(frame[8..]),
                ProducerId = new Guid(frame.Slice(16, 16)),
                ProducerSequence = BinaryPrimitives.ReadInt64LittleEndian(frame[32..]),
                BlockOffset = (int)headerLength,
            };

            error = null;
            return true;
        }
    }
}
