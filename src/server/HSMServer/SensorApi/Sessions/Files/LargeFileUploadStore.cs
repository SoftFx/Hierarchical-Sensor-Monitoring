using HSMDatabase.AccessManager.LargeFiles;
using HSMServer.ApiObjectsConverters;
using HSMServer.Core.LargeFiles;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.SensorApi.Sessions.Files
{
    // The file connection's store (#1884) over the uploads of the large file store (#1883): the one implementation
    // in production. It maps one to one, keeps the store's faults as FileUploadStoreException, and hands the store
    // the connection's end check.
    public sealed class LargeFileUploadStore(ILargeFileUploads uploads, long maxFileBytes) : IFileUploadStore
    {
        public long MaxFileBytes { get; } = maxFileBytes;


        public async Task<FileBeginResult> BeginAsync(FileBeginRequest request, CancellationToken ct)
        {
            var result = await Faults(() => uploads.BeginAsync(new LargeFileBeginRequest(request.ProductId, request.SensorPath, request.Name, request.Size,
                request.SourceModifiedUtc, request.SourceIdentity, request.PieceSize, request.MaxPieceSize,
                Cut(request.ClientName, LargeFileUploadService.MaxOriginLength), Cut(request.RemoteAddress, LargeFileUploadService.MaxOriginLength),
                request.Secure, request.Status.Convert(), Cut(request.Comment, LargeFileUploadService.MaxCommentLength)), ct));

            return new FileBeginResult(Map(result.Outcome), new FileUploadLease(result.Lease.FileId, result.Lease.LeaseId), result.FileId, result.NextPiece,
                result.PieceCount, result.PieceSize, result.RetryAfter, result.Error);
        }

        public async Task<FileAppendResult> AppendAsync(FileUploadLease lease, long pieceNumber, ReadOnlyMemory<byte> storedBytes, ReadOnlyMemory<byte> digest, CancellationToken ct)
        {
            var result = await Faults(() => uploads.AppendAsync(Lease(lease), pieceNumber, storedBytes, digest, ct));

            var outcome = result.Outcome switch
            {
                LargeFileAppendOutcome.Stored => FileAppendOutcome.Stored,
                LargeFileAppendOutcome.AlreadyHave => FileAppendOutcome.AlreadyHave,
                LargeFileAppendOutcome.OutOfOrder => FileAppendOutcome.OutOfOrder,
                LargeFileAppendOutcome.TooMany => FileAppendOutcome.TooMany,
                LargeFileAppendOutcome.Discarded => FileAppendOutcome.Discarded,
                _ => FileAppendOutcome.LeaseLost,
            };

            return new FileAppendResult(outcome, result.NextPiece);
        }

        public Task DiscardAsync(FileUploadLease lease, string reason, CancellationToken ct) => Faults(() => uploads.DiscardAsync(Lease(lease), reason, ct));

        public void Release(FileUploadLease lease) => uploads.Release(Lease(lease));

        public async Task<FileFinishResult> FinishAsync(FileUploadLease lease, byte[] wholeHash, IFilePartChecker checker, CancellationToken ct)
        {
            var result = await Faults(() => uploads.FinishAsync(Lease(lease), wholeHash, new Checker(checker), ct));

            var outcome = result.Outcome switch
            {
                LargeFileFinishOutcome.Verified => FileFinishOutcome.Verified,
                LargeFileFinishOutcome.Mismatch => FileFinishOutcome.Mismatch,
                LargeFileFinishOutcome.Incomplete => FileFinishOutcome.Incomplete,
                LargeFileFinishOutcome.SensorGone => FileFinishOutcome.SensorGone,
                LargeFileFinishOutcome.Checking => FileFinishOutcome.Checking,
                _ => FileFinishOutcome.LeaseLost,
            };

            return new FileFinishResult(outcome, result.FileId, result.ValueTimeUtc, result.NextPiece, result.Error);
        }


        // An outcome this mapping does not know goes to the connection as one it does not know either, which answers
        // it as a fault (503), never as a final refusal.
        private static FileBeginOutcome Map(LargeFileBeginOutcome outcome) => outcome switch
        {
            LargeFileBeginOutcome.Started => FileBeginOutcome.Started,
            LargeFileBeginOutcome.Continue => FileBeginOutcome.Continue,
            LargeFileBeginOutcome.AlreadyStored => FileBeginOutcome.AlreadyStored,
            LargeFileBeginOutcome.Checking => FileBeginOutcome.Checking,
            LargeFileBeginOutcome.NoRoom => FileBeginOutcome.NoRoom,
            LargeFileBeginOutcome.TooLarge => FileBeginOutcome.TooLarge,
            LargeFileBeginOutcome.BadPieceSize => FileBeginOutcome.BadPieceSize,
            LargeFileBeginOutcome.TooManyUploads => FileBeginOutcome.TooManyUploads,
            LargeFileBeginOutcome.TooManyParts => FileBeginOutcome.TooManyParts,
            LargeFileBeginOutcome.Busy => FileBeginOutcome.Busy,
            LargeFileBeginOutcome.SensorRefused => FileBeginOutcome.SensorRefused,
            _ => (FileBeginOutcome)byte.MaxValue,
        };

        private static UploadLease Lease(FileUploadLease lease) => new(lease.FileId, lease.LeaseId);

        // The wire takes a longer comment (a begin must only fit its frame); the value keeps what every value keeps.
        private static string Cut(string text, int limit) => text is null || text.Length <= limit ? text : text[..limit];

        private static async Task<T> Faults<T>(Func<Task<T>> call)
        {
            try
            {
                return await call();
            }
            catch (LargeFileStoreException ex)
            {
                throw new FileUploadStoreException(ex.Message, ex);
            }
        }

        private static async Task Faults(Func<Task> call)
        {
            try
            {
                await call();
            }
            catch (LargeFileStoreException ex)
            {
                throw new FileUploadStoreException(ex.Message, ex);
            }
        }


        private sealed class Checker(IFilePartChecker checker) : ILargeFilePartChecker
        {
            public async Task<LargeFilePartCheck> CheckAsync(ILargeFilePartReader part, byte[] wholeHash, CancellationToken ct)
            {
                var check = await checker.CheckAsync(new Reader(part), wholeHash, ct);

                return new LargeFilePartCheck(check.Matches, check.ContentHash, check.FirstBadPiece, check.Defect);
            }
        }


        private sealed class Reader(ILargeFilePartReader part) : IFilePartReader
        {
            public long Size => part.Record.Size;

            public int PieceSize => part.Record.PieceSize;

            public async IAsyncEnumerable<FileStoredPiece> ReadPiecesAsync([EnumeratorCancellation] CancellationToken ct)
            {
                IAsyncEnumerable<StoredPiece> pieces;

                try
                {
                    pieces = part.ReadPiecesAsync(ct);
                }
                catch (LargeFileStoreException ex)
                {
                    throw new FileUploadStoreException(ex.Message, ex);
                }

                await foreach (var piece in pieces.WithCancellation(ct))
                    yield return new FileStoredPiece(piece.Number, piece.Bytes, piece.Digest);
            }
        }
    }
}
