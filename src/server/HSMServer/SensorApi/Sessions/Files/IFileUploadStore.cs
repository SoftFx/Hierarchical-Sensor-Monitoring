using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMSensorDataObjects;

namespace HSMServer.SensorApi.Sessions.Files
{
    // The store of large files as the file connection (#1884) sees it. It is shaped one to one like the
    // uploads of the large file store of #1883 (ILargeFileUploads), which an adapter maps onto it; the
    // connection's tests run against a fake. Refusals are results; a fault of the store (a write that
    // failed, a full disk in the middle included) throws FileUploadStoreException, and nothing was
    // acknowledged. A broken contract of the caller throws ArgumentException.
    public interface IFileUploadStore
    {
        // The largest file the store takes (LargeFiles.MaxFileSizeBytes), announced in the hello.
        long MaxFileBytes { get; }

        Task<FileBeginResult> BeginAsync(FileBeginRequest request, CancellationToken ct);

        // Appends piece `pieceNumber` as `storedBytes` (the zstd frame, as received) with the SHA-256 of
        // its original bytes; returns once both are on the disk.
        Task<FileAppendResult> AppendAsync(FileUploadLease lease, long pieceNumber, ReadOnlyMemory<byte> storedBytes, ReadOnlyMemory<byte> digest, CancellationToken ct);

        // Removes the upload, its part and its reservation, and ends the lease. False when nothing was removed: a stale
        // lease (another connection holds the upload now) or an upload that is gone.
        Task<bool> DiscardAsync(FileUploadLease lease, string reason, CancellationToken ct);

        // How long the store keeps a lease without an append (LargeFiles.LeaseTimeoutSeconds): past it the connection no
        // longer counts on holding its upload.
        TimeSpan LeaseTimeout { get; }

        // Ends a lease; idempotent; a stale lease is ignored.
        void Release(FileUploadLease lease);

        // Checks the stored pieces with `checker` and, when they match, records the file as verified on
        // the disk and writes its value; answers Verified only after both. Runs on the caller's task for
        // as long as the check takes. A cancelled or faulted check changes nothing on the disk.
        Task<FileFinishResult> FinishAsync(FileUploadLease lease, byte[] wholeHash, IFilePartChecker checker, CancellationToken ct);
    }


    // What names a file: the root product of the access key, the normalized sensor path, the file's name,
    // its original size, the modification time of the source file (UTC), an optional opaque identity of the
    // source file and its mark (ADR-0022 point 20: a secure file is archived under the product's password, a
    // file that is not secure without one; another mark is another upload). `PieceSize` is what the collector
    // cuts the file into; `MaxPieceSize` is what the connection's frame allows. `Status` and `Comment` go into
    // the value (a routed ordinary send keeps its own; a large file of the large-file API sends neither).
    // `ClientName` and `RemoteAddress` are the server's knowledge of the session, for the record.
    public sealed record FileBeginRequest(Guid ProductId, string SensorPath, string Name, long Size, DateTime SourceModifiedUtc, string SourceIdentity,
        int PieceSize, int MaxPieceSize, bool Secure = true, SensorStatus Status = SensorStatus.Ok, string Comment = null,
        string ClientName = null, string RemoteAddress = null);


    public readonly record struct FileUploadLease(Guid FileId, Guid LeaseId);


    public enum FileBeginOutcome : byte
    {
        Started,
        Continue,
        AlreadyStored,
        Checking,
        NoRoom,
        TooLarge,
        BadPieceSize,
        TooManyUploads,
        Busy,
        SensorRefused,

        // The product has as many unfinished uploads as the store keeps for one product (#1883); not final.
        TooManyParts,
    }


    // `PieceSize` is the record's piece size on a continue (and the request's on a start).
    public sealed record FileBeginResult(FileBeginOutcome Outcome, FileUploadLease Lease, Guid FileId, long NextPiece, long PieceCount, int PieceSize,
        TimeSpan? RetryAfter, string Error);


    public enum FileAppendOutcome : byte
    {
        Stored,
        AlreadyHave,
        OutOfOrder,
        TooMany,
        LeaseLost,
        Discarded,
    }


    public sealed record FileAppendResult(FileAppendOutcome Outcome, long NextPiece);


    public enum FileFinishOutcome : byte
    {
        Verified,
        Mismatch,
        Incomplete,
        LeaseLost,
        SensorGone,
        Checking,
    }


    public sealed record FileFinishResult(FileFinishOutcome Outcome, Guid FileId, DateTime? ValueTimeUtc, long NextPiece, string Error);


    // One stored piece as it was appended: its bytes as received and the digest that came with it.
    public readonly record struct FileStoredPiece(long Number, ReadOnlyMemory<byte> Bytes, ReadOnlyMemory<byte> Digest);


    // A part read piece by piece, in order, one pass.
    public interface IFilePartReader
    {
        long Size { get; }

        int PieceSize { get; }

        IAsyncEnumerable<FileStoredPiece> ReadPiecesAsync(CancellationToken ct);
    }


    // The end check (ADR-0022 point 7). `Matches` is false for every defect of the content, with the first
    // bad piece and what was wrong; `ContentHash` is the SHA-256 of the original bytes, what the value
    // shows. A fault of reading throws.
    public sealed record FilePartCheck(bool Matches, byte[] ContentHash, long? FirstBadPiece, string Defect);


    public interface IFilePartChecker
    {
        Task<FilePartCheck> CheckAsync(IFilePartReader part, byte[] wholeHash, CancellationToken ct);
    }


    public sealed class FileUploadStoreException(string message, Exception inner = null) : Exception(message, inner);


    // A stored piece that cannot be read back (its index does not match the part, the part ends before it): thrown by a
    // part reader, taken by the end check as a defect of the content at that piece, never as a fault.
    public sealed class FilePieceDefectException(long piece, string message, Exception inner = null) : Exception(message, inner)
    {
        public long Piece { get; } = piece;
    }
}
