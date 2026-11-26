using HSMServer.SensorApi.Sessions.Files;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace HSMServer.Core.Tests.SensorApi
{
    // The piece of a file connection (#1884) without a connection: the header, the checks of a zstd frame
    // at arrival and the end check.
    public class FilePieceTests
    {
        private const int PieceSize = 64 * 1024;


        [Fact]
        public void AHeader_IsReadFieldByField()
        {
            var upload = Guid.NewGuid();
            var piece = TestPiece.Of(3, new byte[100]);
            var frame = piece.Frame(42, FilePiece.FormatUploadId(upload));

            Assert.True(FilePiece.TryRead(frame, out var header, out var error), error);
            Assert.Equal(FilePieceKind.Piece, header.Kind);
            Assert.Equal(42, header.Id);
            Assert.Equal(upload, header.UploadId);
            Assert.Equal(3, header.Number);
            Assert.Equal(100u, header.DecompressedLength);
            Assert.Equal((uint)piece.Compressed.Length, header.CompressedLength);
            Assert.Equal(FilePiece.ComputeChecksum(piece.Compressed), header.Checksum);
            Assert.Equal(88, header.PayloadOffset);
            Assert.Equal(piece.Digest, FilePiece.DigestOf(frame).ToArray());
        }

        [Fact]
        public void AnUploadId_RoundTripsThroughItsHex()
        {
            var id = Guid.NewGuid();

            Assert.True(FilePiece.TryParseUploadId(FilePiece.FormatUploadId(id), out var parsed));
            Assert.Equal(id, parsed);
            Assert.False(FilePiece.TryParseUploadId("zz" + new string('0', 30), out _));
            Assert.False(FilePiece.TryParseUploadId("00", out _));
        }

        [Fact]
        public void TheLengthOfAPiece_IsThePieceSizeAndTheLastTheRemainder()
        {
            Assert.Equal(3, FilePiece.PieceCount(2 * PieceSize + 1, PieceSize));
            Assert.Equal(0, FilePiece.PieceCount(0, PieceSize));
            Assert.Equal(PieceSize, FilePiece.LengthOf(1, 2 * PieceSize + 1, PieceSize));
            Assert.Equal(1, FilePiece.LengthOf(2, 2 * PieceSize + 1, PieceSize));
            Assert.Equal(0, FilePiece.LengthOf(3, 2 * PieceSize + 1, PieceSize));
            Assert.Equal(0, FilePiece.LengthOf(-1, 2 * PieceSize + 1, PieceSize));
        }

        [Fact]
        public void TheWindowBound_IsThePieceSizeRoundedUpToAPowerOfTwo()
        {
            Assert.Equal(16, FilePiece.MaxWindowLog(64 * 1024));
            Assert.Equal(22, FilePiece.MaxWindowLog(4_173_824));
            Assert.Equal(17, FilePiece.MaxWindowLog(64 * 1024 + 1));
        }

        [Fact]
        public void AFrameOfZstd_PassesTheChecksAtArrival_AndDecompressesToItsBytes()
        {
            var original = RandomBytes(PieceSize);
            var piece = TestPiece.Of(0, original);

            Assert.Null(FilePiece.CheckFrame(piece.Compressed, PieceSize, FilePiece.MaxWindowLog(PieceSize)));

            var output = new byte[PieceSize];

            Assert.Null(FilePiece.Decompress(piece.Compressed, output, FilePiece.MaxWindowLog(PieceSize)));
            Assert.Equal(original, output);
        }

        // A frame that asks for a window larger than the piece is refused before anything is decompressed.
        [Fact]
        public void AFrameThatAsksForALargeWindow_IsRefusedAtArrival()
        {
            var frame = FrameWithWindow(windowLog: 27, contentSize: PieceSize);

            Assert.Contains("window", FilePiece.CheckFrame(frame, PieceSize, FilePiece.MaxWindowLog(PieceSize)));
            Assert.Null(FilePiece.CheckFrame(FrameWithWindow(windowLog: 16, contentSize: PieceSize), PieceSize, FilePiece.MaxWindowLog(PieceSize)));
        }

        [Fact]
        public void ASkippableFrame_IsNotAPiece()
        {
            var frame = new byte[16];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, 0x184D2A50);
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), 8);

            Assert.NotNull(FilePiece.CheckFrame(frame, 8, 16));
            Assert.NotNull(FilePiece.CheckFrame([1, 2, 3, 4, 5], 8, 16));
            Assert.NotNull(FilePiece.CheckFrame([], 8, 16));
        }

        [Fact]
        public void AFrameThatDecompressesToMoreThanItsLength_IsCaughtByTheBoundedBuffer()
        {
            var piece = TestPiece.Of(0, RandomBytes(1000));

            Assert.Contains("more than", FilePiece.Decompress(piece.Compressed, new byte[999], 16));
        }


        // === The end check ===

        [Fact]
        public async Task TheEndCheck_OfAWholeFile_MatchesAndGivesTheHashOfTheOriginalBytes()
        {
            var file = TestFile.Random("a", 3 * PieceSize + 17);
            var checker = new FilePieceChecker();

            var check = await checker.CheckAsync(Part(file), Convert.FromHexString(file.Hash()), CancellationToken.None);

            Assert.True(check.Matches, check.Defect);
            Assert.Equal(SHA256.HashData(file.Bytes), check.ContentHash);
            Assert.Same(check, checker.Last);
        }

        public static TheoryData<string, string> Defects => new()
        {
            { "missing piece", "piece 3: 3 of 4 pieces are stored" },
            { "digest", "piece 1: its bytes do not match its digest" },
            { "short digest", "piece 1: its stored digest is not 32 bytes" },
            { "frame", "piece 1: the zstd frame states 65535 bytes, the piece has 65536" },
            { "order", "piece 1: piece 2 is stored where piece 1 of 4 belongs" },
        };

        // Every defect of the content is a non-match naming the first bad piece, never an exception.
        [Theory]
        [MemberData(nameof(Defects))]
        public async Task TheEndCheck_NamesTheFirstBadPiece(string defect, string expected)
        {
            var file = TestFile.Random("a", 3 * PieceSize + 17);
            var pieces = Enumerable.Range(0, 4).Select(i => file.Piece(i)).Select((p, i) => new FileStoredPiece(i, p.Compressed, p.Digest)).ToList();

            switch (defect)
            {
                case "missing piece":
                    pieces.RemoveAt(3);
                    break;
                case "digest":
                    pieces[1] = pieces[1] with { Digest = new byte[32] };
                    break;
                case "short digest":
                    pieces[1] = pieces[1] with { Digest = new byte[31] };
                    break;
                case "frame":
                    pieces[1] = pieces[1] with { Bytes = TestPiece.Of(1, file.Piece(1).Original[..^1]).Compressed };
                    break;
                case "order":
                    pieces[1] = pieces[2];
                    break;
            }

            var check = await new FilePieceChecker().CheckAsync(new Reader(file.Bytes.Length, PieceSize, pieces), Convert.FromHexString(file.Hash()), CancellationToken.None);

            Assert.False(check.Matches);
            Assert.Equal(expected, check.Defect);
            Assert.Null(check.ContentHash);
        }

        [Fact]
        public async Task TheEndCheck_OfAWrongWholeHash_DoesNotMatch()
        {
            var file = TestFile.Random("a", PieceSize + 1);

            var check = await new FilePieceChecker().CheckAsync(Part(file), new byte[32], CancellationToken.None);

            Assert.False(check.Matches);
            Assert.Null(check.FirstBadPiece);
            Assert.Equal("the hash of the whole file does not match", check.Defect);
        }


        private static Reader Part(TestFile file) =>
            new(file.Bytes.Length, file.PieceBytes, Enumerable.Range(0, (int)file.PieceCount).Select(i => new FileStoredPiece(i, file.Piece(i).Compressed, file.Piece(i).Digest)).ToList());

        private static byte[] RandomBytes(int length)
        {
            var bytes = new byte[length];
            new Random(5).NextBytes(bytes);
            return bytes;
        }

        // A frame by the format (RFC 8878) with a window descriptor and a 4-byte content size, holding one raw
        // block of the whole content.
        private static byte[] FrameWithWindow(int windowLog, int contentSize)
        {
            var frame = new byte[4 + 1 + 1 + 4 + 3 + contentSize];

            BinaryPrimitives.WriteUInt32LittleEndian(frame, 0xFD2FB528);
            frame[4] = 0x80;                          // Frame_Content_Size_flag 2 (4 bytes), not a single segment
            frame[5] = (byte)((windowLog - 10) << 3); // Window_Descriptor: exponent, no mantissa
            BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(6), (uint)contentSize);

            var header = (contentSize << 3) | 1;      // Last_Block, Raw_Block
            frame[10] = (byte)header;
            frame[11] = (byte)(header >> 8);
            frame[12] = (byte)(header >> 16);

            return frame;
        }


        private sealed class Reader(long size, int pieceSize, IReadOnlyList<FileStoredPiece> pieces) : IFilePartReader
        {
            public long Size => size;

            public int PieceSize => pieceSize;

            public async IAsyncEnumerable<FileStoredPiece> ReadPiecesAsync([EnumeratorCancellation] CancellationToken ct)
            {
                foreach (var piece in pieces)
                {
                    await Task.Yield();
                    yield return piece;
                }
            }
        }
    }
}
