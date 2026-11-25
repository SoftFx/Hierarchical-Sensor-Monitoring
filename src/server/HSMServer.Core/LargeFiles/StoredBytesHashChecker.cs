using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using HSMDatabase.AccessManager.LargeFiles;

namespace HSMServer.Core.LargeFiles
{
    /// <summary>
    /// The end check of step 1 (#1883): SHA-256 over the stored bytes as they are, compared with the whole hash. For tests
    /// and for a part that is stored uncompressed; the file connection (#1884) supplies the production checker, which
    /// decompresses each piece and checks its digest.
    /// </summary>
    public sealed class StoredBytesHashChecker : ILargeFilePartChecker
    {
        public async Task<bool> CheckAsync(ILargeFilePartReader part, byte[] wholeHash, CancellationToken ct)
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            await foreach (var piece in part.ReadPiecesAsync(ct))
                hash.AppendData(piece.Bytes.Span);

            return CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), wholeHash);
        }
    }
}
