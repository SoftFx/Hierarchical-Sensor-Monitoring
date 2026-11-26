using System;
using System.Buffers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions.Files
{
    // The end check of an upload (#1884, ADR-0022 point 7): every stored piece is read, its zstd frame
    // checked and decompressed into a buffer of exactly its length with a bounded window, and its SHA-256
    // compared with the digest that came with it. The hash of the whole file on the wire is the SHA-256 of
    // the pieces' digests in order; the hash the value shows is the SHA-256 of the original bytes, made
    // here on the way.
    //
    // Every defect of the content is a non-match with the first bad piece; only a fault of reading throws.
    internal sealed class FilePieceChecker : IFilePartChecker
    {
        // What the last check of this checker found: one checker per end check, so the connection can tell the
        // collector which piece was bad whatever text the store gives its mismatch.
        public FilePartCheck Last { get; private set; }


        public async Task<FilePartCheck> CheckAsync(IFilePartReader part, byte[] wholeHash, CancellationToken ct)
        {
            var result = await CheckCoreAsync(part, wholeHash, ct);

            Last = result;
            return result;
        }

        private static async Task<FilePartCheck> CheckCoreAsync(IFilePartReader part, byte[] wholeHash, CancellationToken ct)
        {
            var size = part.Size;
            var pieceSize = part.PieceSize;

            if (size < 0 || pieceSize < FilePiece.MinPieceBytes)
                return new FilePartCheck(false, null, null, $"the record has a size of {size} and a piece size of {pieceSize}");

            var total = FilePiece.PieceCount(size, pieceSize);
            var maxWindowLog = FilePiece.MaxWindowLog(pieceSize);

            using var content = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var digests = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            var buffer = ArrayPool<byte>.Shared.Rent(pieceSize);
            var expected = 0L;

            try
            {
                await foreach (var piece in part.ReadPiecesAsync(ct))
                {
                    if (piece.Number != expected || expected >= total)
                        return Bad(expected, $"piece {piece.Number} is stored where piece {expected} of {total} belongs");

                    var length = (int)FilePiece.LengthOf(expected, size, pieceSize);

                    if (CheckPiece(piece, buffer, length, maxWindowLog, content, digests) is { } defect)
                        return Bad(expected, defect);

                    expected++;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            if (expected != total)
                return Bad(expected, $"{expected} of {total} pieces are stored");

            if (!digests.GetHashAndReset().AsSpan().SequenceEqual(wholeHash))
                return new FilePartCheck(false, null, null, "the hash of the whole file does not match");

            return new FilePartCheck(true, content.GetHashAndReset(), null, null);
        }

        // One piece: its frame, its bytes, its digest; then both hashes take it. Null when it holds.
        private static string CheckPiece(FileStoredPiece piece, byte[] buffer, int length, int maxWindowLog, IncrementalHash content, IncrementalHash digests)
        {
            if (piece.Digest.Length != FilePiece.DigestLength)
                return "its stored digest is not 32 bytes";

            if (FilePiece.CheckFrame(piece.Bytes.Span, length, maxWindowLog) is { } frameError)
                return frameError;

            var original = buffer.AsSpan(0, length);

            if (FilePiece.Decompress(piece.Bytes.Span, original, maxWindowLog) is { } decompressError)
                return decompressError;

            Span<byte> digest = stackalloc byte[FilePiece.DigestLength];
            SHA256.HashData(original, digest);

            if (!digest.SequenceEqual(piece.Digest.Span))
                return "its bytes do not match its digest";

            content.AppendData(original);
            digests.AppendData(digest);

            return null;
        }

        private static FilePartCheck Bad(long piece, string defect) => new(false, null, piece, $"piece {piece}: {defect}");
    }
}
