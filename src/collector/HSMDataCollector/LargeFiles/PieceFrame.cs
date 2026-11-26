using System;
using System.Buffers.Binary;
using System.Globalization;
using System.IO.Hashing;
using System.Security.Cryptography;
using ZstdSharp;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>
    /// The binary frame of one piece on the file connection (wire contract, "File connection", version 1): an 88-byte
    /// little-endian header and one zstd frame of the piece, compressed at level 3 by the zstd library the collector bundles
    /// (ZstdSharp, as table blocks). Owns its buffers, sized for one piece: one piece in memory, plus its compressed form.
    /// Not thread-safe; one per upload.
    /// </summary>
    internal sealed class PieceFrame : IDisposable
    {
        internal const int HeaderSize = 88;
        internal const int CompressionLevel = 3;

        private const byte FrameVersion = 1;
        internal const byte KindPiece = 1;
        internal const byte KindProbe = 2;

        private readonly Compressor _compressor = new Compressor(CompressionLevel);
        private readonly SHA256 _sha = SHA256.Create();

        internal PieceFrame(int pieceBytes)
        {
            Piece = new byte[pieceBytes];
            Frame = new byte[HeaderSize + Compressor.GetCompressBound(pieceBytes)];
        }

        /// <summary>The source bytes of the piece being sent; the reader fills it.</summary>
        internal byte[] Piece { get; }

        /// <summary>The frame: header and compressed piece; <see cref="FrameLength"/> bytes of it are sent.</summary>
        internal byte[] Frame { get; }

        internal int FrameLength { get; private set; }

        /// <summary>The SHA-256 of the last piece's source bytes.</summary>
        internal byte[] Digest { get; private set; }

        /// <summary>The largest frame a piece of <paramref name="pieceBytes"/> can make, header included.</summary>
        internal static long MaxFrameBytes(int pieceBytes) => HeaderSize + Compressor.GetCompressBound(pieceBytes);

        /// <summary>Compresses the first <paramref name="length"/> bytes of <see cref="Piece"/> and writes the frame of piece <paramref name="number"/>.</summary>
        internal void Build(long requestId, byte[] upload, long number, int length)
        {
            Digest = ComputeDigest(Piece, length);

            var compressed = _compressor.Wrap(new ReadOnlySpan<byte>(Piece, 0, length), new Span<byte>(Frame, HeaderSize, Frame.Length - HeaderSize));
            var body = new ReadOnlySpan<byte>(Frame, HeaderSize, compressed);

            WriteHeader(new Span<byte>(Frame, 0, HeaderSize), KindPiece, requestId, upload, number, (uint)length, (uint)compressed, Crc32.HashToUInt32(body), Digest);
            FrameLength = HeaderSize + compressed;
        }

        internal byte[] ComputeDigest(byte[] source, int length) => _sha.ComputeHash(source, 0, length);

        /// <summary>A probe (kind 2): the header with zeros after the request id.</summary>
        internal static byte[] Probe(long requestId)
        {
            var header = new byte[HeaderSize];
            WriteHeader(header, KindProbe, requestId, null, 0, 0, 0, 0, null);
            return header;
        }

        internal static void WriteHeader(Span<byte> header, byte kind, long requestId, byte[] upload, long number, uint length, uint compressed, uint crc, byte[] digest)
        {
            header.Clear();
            header[0] = FrameVersion;
            header[1] = kind;
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(4, 4), HeaderSize);
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(8, 8), requestId);

            if (kind == KindProbe)
                return;

            new ReadOnlySpan<byte>(upload, 0, 16).CopyTo(header.Slice(16, 16));
            BinaryPrimitives.WriteInt64LittleEndian(header.Slice(32, 8), number);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(40, 4), length);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(44, 4), compressed);
            BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(48, 4), crc);
            new ReadOnlySpan<byte>(digest, 0, 32).CopyTo(header.Slice(56, 32));
        }

        /// <summary>The 16 bytes of an upload id given as 32 hex digits; null when it is not that.</summary>
        internal static byte[] ParseUploadId(string hex)
        {
            if (hex == null || hex.Length != 32)
                return null;

            var bytes = new byte[16];

            for (var i = 0; i < 16; i++)
                if (!byte.TryParse(hex.Substring(i * 2, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out bytes[i]))
                    return null;

            return bytes;
        }

        /// <summary>The hash of a whole file for <c>end</c>: the SHA-256 of the piece digests in piece order, lower-case hex.</summary>
        internal static string FileHash(byte[] digests, long pieces)
        {
            using (var sha = SHA256.Create())
                return Hex(sha.ComputeHash(digests, 0, checked((int)(pieces * 32))));
        }

        internal static string Hex(byte[] bytes)
        {
            var chars = new char[bytes.Length * 2];
            const string digits = "0123456789abcdef";

            for (var i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = digits[bytes[i] >> 4];
                chars[i * 2 + 1] = digits[bytes[i] & 15];
            }

            return new string(chars);
        }

        public void Dispose()
        {
            _compressor.Dispose();
            _sha.Dispose();
        }
    }
}
