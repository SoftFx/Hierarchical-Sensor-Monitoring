using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.LargeFiles;

namespace HSMServer.Core.Tests.LargeFiles
{
    /// <summary>
    /// The test checker of step 1 (#1883), in the tests only (production gets its checker from the file connection, #1884):
    /// SHA-256 over the stored bytes as they are, compared with the whole hash, and given
    /// as the content hash. For tests and for a part that is stored uncompressed; the file connection (#1884) supplies the
    /// production checker, which decompresses each piece and checks its digest.
    /// </summary>
    public sealed class StoredBytesHashChecker : ILargeFilePartChecker
    {
        public async Task<LargeFilePartCheck> CheckAsync(ILargeFilePartReader part, byte[] wholeHash, CancellationToken ct)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await foreach (var piece in part.ReadPiecesAsync(ct))
                hash.AppendData(piece.Bytes.Span);

            var content = hash.GetHashAndReset();

            return wholeHash is { Length: 32 } && CryptographicOperations.FixedTimeEquals(content, wholeHash)
                ? new LargeFilePartCheck(true, content, null, null)
                : new LargeFilePartCheck(false, content, null, "The SHA-256 of the stored bytes is not the hash sent.");
        }
    }
}
