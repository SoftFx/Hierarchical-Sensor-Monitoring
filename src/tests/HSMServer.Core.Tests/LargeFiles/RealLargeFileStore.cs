using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.LargeFiles;
using HSMDatabase.LevelDB.LargeFiles;
using HSMServer.Core.LargeFiles.Archives;
using Xunit;
using ZstdSharp;

namespace HSMServer.Core.Tests.LargeFiles
{
    /// <summary>
    /// The large file store of #1883 over a temporary folder, with the archive seam of #1885 over it: files are made
    /// verified through the store's own upload path (begin, append, the end check, verified, published), each piece a zstd
    /// frame of the original bytes, as the collector sends them.
    /// </summary>
    internal sealed class RealLargeFileStore : IDisposable
    {
        public const int Piece = LargeFileStore.MinPieceSize;

        public static readonly DateTime Modified = new(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc);


        public RealLargeFileStore()
        {
            Store = new LargeFileStore(Root)
            {
                FreeBytes = () => long.MaxValue / 4,
                Limits = new LargeFileStoreLimits { MinFreeBytes = 0 },
            };

            Adapter = new LargeFileStoreArchiveAdapter(Store);
        }


        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"hsm-real-large-files-{Guid.NewGuid():N}");

        public LargeFileStore Store { get; }

        public LargeFileStoreArchiveAdapter Adapter { get; }


        /// <summary>Uploads the content through the store and leaves it verified (its event raised); returns the file id.</summary>
        public Guid Verify(Guid productId, Guid sensorId, byte[] content, string name = "dump.dmp", bool secure = true)
        {
            var key = new LargeFileUploadKey(productId, sensorId, name, content.Length, Modified, null, secure);
            var begin = Store.Begin(key, Path.GetExtension(name).TrimStart('.'), Piece, Piece);
            Assert.Equal(LargeFileBeginOutcome.Started, begin.Outcome);

            using (var compressor = new Compressor(3))
            {
                for (var i = 0; i * Piece < content.Length; i++)
                {
                    var original = content.AsSpan(i * Piece, Math.Min(Piece, content.Length - i * Piece));
                    var stored = compressor.Wrap(original).ToArray();

                    Assert.Equal(LargeFileAppendOutcome.Stored, Store.Append(begin.Lease, i, stored, SHA256.HashData(original)).Outcome);
                }
            }

            var ticket = Store.StartFinish(begin.Lease);
            Assert.Equal(LargeFileFinishStart.Ready, ticket.Outcome);

            try
            {
                var check = new ZstdContentChecker().CheckAsync(ticket.Reader, SHA256.HashData(content), CancellationToken.None).GetAwaiter().GetResult();
                Assert.True(check.Matches, check.Defect);

                Store.MarkVerified(begin.FileId, check.ContentHash, DateTime.UtcNow);
            }
            finally
            {
                Store.EndFinish(begin.Lease);
                Store.Release(begin.Lease);
            }

            Store.PublishVerified(begin.FileId);

            return begin.FileId;
        }

        /// <summary>Begins an upload and leaves it uploading; returns the file id.</summary>
        public Guid BeginOnly(Guid productId, Guid sensorId, int size, string name = "partial.dmp")
        {
            var begin = Store.Begin(new LargeFileUploadKey(productId, sensorId, name, size, Modified, null), "dmp", Piece, Piece);
            Assert.Equal(LargeFileBeginOutcome.Started, begin.Outcome);

            var original = new byte[Math.Min(size, Piece)];
            using var compressor = new Compressor(3);
            Assert.Equal(LargeFileAppendOutcome.Stored, Store.Append(begin.Lease, 0, compressor.Wrap(original).ToArray(), SHA256.HashData(original)).Outcome);

            return begin.FileId;
        }

        public static byte[] Content(int length, int seed = 7)
        {
            // Half random, half repeated text: compressible like a dump, and different for every seed.
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes.AsSpan(0, length / 2));

            for (var i = length / 2; i < length; i++)
                bytes[i] = (byte)"large file content "[i % 19];

            return bytes;
        }

        public void Dispose()
        {
            Adapter.Dispose();
            Store.Dispose();

            try
            {
                if (Directory.Exists(Root))
                    Directory.Delete(Root, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }


        // The end check as the file connection makes it: every piece decompressed, the digest of each and the hash of the
        // whole content compared.
        private sealed class ZstdContentChecker : ILargeFilePartChecker
        {
            public async Task<LargeFilePartCheck> CheckAsync(ILargeFilePartReader part, byte[] wholeHash, CancellationToken ct)
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using var decompressor = new Decompressor();

                await foreach (var piece in part.ReadPiecesAsync(ct))
                {
                    var original = decompressor.Unwrap(piece.Bytes.Span).ToArray();

                    if (!SHA256.HashData(original).AsSpan().SequenceEqual(piece.Digest.Span))
                        return new LargeFilePartCheck(false, null, piece.Number, $"The digest of piece {piece.Number} does not match.");

                    hash.AppendData(original);
                }

                var content = hash.GetHashAndReset();

                return CryptographicOperations.FixedTimeEquals(content, wholeHash)
                    ? new LargeFilePartCheck(true, content, null, null)
                    : new LargeFilePartCheck(false, content, null, "The SHA-256 of the content is not the hash sent.");
            }
        }
    }
}
