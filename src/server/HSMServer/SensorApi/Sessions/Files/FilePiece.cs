using System;
using System.Buffers.Binary;
using System.IO.Hashing;
using ZstdSharp;
using ZstdSharp.Unsafe;

namespace HSMServer.SensorApi.Sessions.Files
{
    internal enum FilePieceKind : byte
    {
        Piece = 1,

        // The header alone, answered with an ordinary ack: lets a collector see at once that binary
        // frames reach the server through whatever sits in between.
        Probe = 2,
    }


    // The header of a binary frame on a file connection (#1884), version 1, little-endian:
    //
    //   0   1  frame version, 1
    //   1   1  kind: 1 piece, 2 probe
    //   2   2  reserved, 0
    //   4   4  header length (u32): where the zstd frame starts; 88 in version 1, at most 1,024
    //   8   8  request id (i64)
    //  16  16  upload id: the 16 bytes whose hex is the "upload" of the begin answer
    //  32   8  piece number (i64, >= 0)
    //  40   4  decompressed length (u32): the piece size, the last piece the remainder
    //  44   4  compressed length (u32): the bytes after the header
    //  48   4  CRC-32 (IEEE) of the compressed bytes
    //  52   4  reserved, 0
    //  56  32  SHA-256 of the piece's decompressed bytes
    //  88   …  one zstd frame (none for a probe)
    //
    // Documented in aicontext/features/api/wire-contract/feature.md, which is the contract.
    internal readonly struct FilePiece
    {
        public const byte Version = 1;
        public const int HeaderLength = 88;
        public const int MaxHeaderLength = 1024;
        public const int DigestLength = 32;

        // The smallest piece a collector may cut a file into.
        public const int MinPieceBytes = 64 * 1024;

        public FilePieceKind Kind { get; init; }

        public long Id { get; init; }

        public Guid UploadId { get; init; }

        public long Number { get; init; }

        public uint DecompressedLength { get; init; }

        public uint CompressedLength { get; init; }

        public uint Checksum { get; init; }

        // Where the zstd frame starts in the frame.
        public int PayloadOffset { get; init; }


        // The piece size a frame of `maxFrameBytes` carries: the largest multiple of 4 KiB that, compressed
        // at worst (bytes that do not compress), still fits the frame with its header.
        public static int PieceBytesFor(int maxFrameBytes)
        {
            var pieceBytes = (int)((maxFrameBytes - (long)HeaderLength) * 256 / 257) & ~4095;

            while (pieceBytes > 0 && HeaderLength + CompressBound(pieceBytes) > maxFrameBytes)
                pieceBytes -= 4096;

            return pieceBytes;
        }

        // The most zstd can make of `length` bytes (ZSTD_compressBound).
        public static long CompressBound(int length) => (long)Compressor.GetCompressBound(length);

        // The largest window a piece of an upload with this piece size may ask for: the piece size rounded up
        // to a power of two, what zstd picks itself for one frame of known size.
        public static int MaxWindowLog(int pieceSize)
        {
            var log = 10;

            while ((1L << log) < pieceSize)
                log++;

            return log;
        }

        // The original length piece `number` of a file of `size` cut into `pieceSize` has; 0 for a number
        // past the last piece.
        public static long LengthOf(long number, long size, int pieceSize)
        {
            var start = number * pieceSize;

            return number < 0 || start >= size ? 0 : Math.Min(pieceSize, size - start);
        }

        public static long PieceCount(long size, int pieceSize) => (size + pieceSize - 1) / pieceSize;

        public static string FormatUploadId(Guid id) => Convert.ToHexString(id.ToByteArray()).ToLowerInvariant();

        public static bool TryParseUploadId(string text, out Guid id)
        {
            id = Guid.Empty;

            if (text is null || text.Length != 32)
                return false;

            try
            {
                id = new Guid(Convert.FromHexString(text));
                return true;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public static uint ComputeChecksum(ReadOnlySpan<byte> compressed) => Crc32.HashToUInt32(compressed);


        // False with the reason when the header cannot be trusted: there is then no request id to answer, and
        // the caller closes the connection (1002).
        public static bool TryRead(ReadOnlySpan<byte> frame, out FilePiece header, out string error)
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

            var kind = (FilePieceKind)frame[1];

            if (kind is not (FilePieceKind.Piece or FilePieceKind.Probe))
            {
                error = $"binary frame kind {frame[1]} is not supported";
                return false;
            }

            if (frame[2] != 0 || frame[3] != 0 || BinaryPrimitives.ReadUInt32LittleEndian(frame[52..]) != 0)
            {
                error = "the reserved bytes of a binary frame are not zero";
                return false;
            }

            var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(frame[4..]);

            if (headerLength < HeaderLength || headerLength > MaxHeaderLength || headerLength > (uint)frame.Length)
            {
                error = $"the header length {headerLength} of a binary frame is outside {HeaderLength}..{Math.Min(MaxHeaderLength, frame.Length)}";
                return false;
            }

            header = new FilePiece
            {
                Kind = kind,
                Id = BinaryPrimitives.ReadInt64LittleEndian(frame[8..]),
                UploadId = new Guid(frame.Slice(16, 16)),
                Number = BinaryPrimitives.ReadInt64LittleEndian(frame[32..]),
                DecompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(frame[40..]),
                CompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(frame[44..]),
                Checksum = BinaryPrimitives.ReadUInt32LittleEndian(frame[48..]),
                PayloadOffset = (int)headerLength,
            };

            error = null;
            return true;
        }

        public static ReadOnlySpan<byte> DigestOf(ReadOnlySpan<byte> frame) => frame.Slice(56, DigestLength);


        // The zstd frame of a piece, checked without decompressing it: exactly one zstd frame (not a skippable
        // one) over every byte, stating its content size, which is `expectedLength`, and asking for a window
        // of at most 2^`maxWindowLog`. All of it read by zstd's own functions. Null when it holds, else what
        // is wrong.
        public static unsafe string CheckFrame(ReadOnlySpan<byte> compressed, long expectedLength, int maxWindowLog)
        {
            if (compressed.IsEmpty)
                return "the piece has no zstd frame";

            ZSTD_frameHeader frameHeader;

            fixed (byte* source = compressed)
            {
                var headerResult = Methods.ZSTD_getFrameHeader(&frameHeader, source, (nuint)compressed.Length);

                if (Methods.ZSTD_isError(headerResult))
                    return "the zstd frame header is invalid";

                if (headerResult != 0)
                    return "the zstd frame header is incomplete";

                if (frameHeader.frameType != ZSTD_frameType_e.ZSTD_frame)
                    return "the piece is not a zstd frame";

                if (frameHeader.frameContentSize == ulong.MaxValue)
                    return "the zstd frame does not state its content size";

                if (frameHeader.frameContentSize != (ulong)expectedLength)
                    return $"the zstd frame states {frameHeader.frameContentSize} bytes, the piece has {expectedLength}";

                if (frameHeader.windowSize > 1UL << maxWindowLog)
                    return $"the zstd frame asks for a window of {frameHeader.windowSize} bytes, more than {1UL << maxWindowLog}";

                var frameSize = Methods.ZSTD_findFrameCompressedSize(source, (nuint)compressed.Length);

                if (Methods.ZSTD_isError(frameSize) || frameSize != (nuint)compressed.Length)
                    return "the piece is not exactly one zstd frame";
            }

            return null;
        }

        // Decompresses a piece into `destination`, which has exactly its original length, with the decoder's
        // window limited to 2^`maxWindowLog`. Null when it holds, else what is wrong: a frame that
        // decompresses to more or fewer bytes, or not at all.
        public static string Decompress(ReadOnlySpan<byte> compressed, Span<byte> destination, int maxWindowLog)
        {
            int written;

            try
            {
                using var decompressor = new Decompressor();

                decompressor.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, maxWindowLog);
                written = decompressor.Unwrap(compressed, destination);
            }
            catch (ZstdException ex) when (ex.Code == ZSTD_ErrorCode.ZSTD_error_dstSize_tooSmall)
            {
                return $"the piece decompresses to more than its {destination.Length} bytes";
            }
            catch (ZstdException ex)
            {
                return $"the piece does not decompress: {ex.Message}";
            }

            return written == destination.Length ? null : $"the piece decompresses to {written} bytes, not {destination.Length}";
        }
    }
}
