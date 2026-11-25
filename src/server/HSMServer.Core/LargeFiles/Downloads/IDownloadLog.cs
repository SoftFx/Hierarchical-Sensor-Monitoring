using System;
using System.Collections.Generic;

namespace HSMServer.Core.LargeFiles.Downloads
{
    public enum DownloadChannel : byte
    {
        Web = 1,
        Api = 2,
    }


    /// <summary>Who asked for a download: a signed-in user, or an API token acting as its owner.</summary>
    public sealed record DownloadCaller(DownloadChannel Channel, Guid UserId, string UserName, Guid? TokenId, string TokenName, string ClientIp)
    {
        /// <summary>The identity the coalescing window is kept for: the user, or the token.</summary>
        public string Key => TokenId is { } token ? $"t:{token:N}" : $"u:{UserId:N}";
    }


    /// <summary>One record of the download log (ADR-0022 point 15): a download was allowed, not that it completed.</summary>
    public sealed record DownloadRecord
    {
        public Guid Id { get; init; }

        public DateTime TimeUtc { get; init; }

        public Guid ProductId { get; init; }

        public Guid SensorId { get; init; }

        public Guid FileId { get; init; }

        public Guid ArchiveId { get; init; }

        public int PasswordVersion { get; init; }

        public string FileName { get; init; }

        public DownloadChannel Channel { get; init; }

        public Guid UserId { get; init; }

        public string UserName { get; init; }

        public Guid? TokenId { get; init; }

        public string TokenName { get; init; }

        public string ClientIp { get; init; }
    }


    /// <summary>What is downloaded, as the download decided it.</summary>
    public sealed record DownloadTarget(Guid ProductId, Guid SensorId, Guid FileId, Guid ArchiveId, int PasswordVersion, string FileName);


    /// <summary>A record could not be written: the download is refused.</summary>
    public sealed class DownloadLogException(string message, Exception inner = null) : Exception(message, inner);


    /// <summary>
    /// The download log: a store of its own, not the journal of configuration changes. A download that cannot be recorded is
    /// refused. The requests of one caller for one archive make one record while they keep coming within a sliding window.
    /// </summary>
    public interface IDownloadLog
    {
        /// <summary>
        /// Records a download unless the caller asked for this archive within the window (each request renews it). Returns
        /// true when a record was written. Throws <see cref="DownloadLogException"/> when it cannot be written.
        /// </summary>
        bool Record(DownloadCaller caller, DownloadTarget target, DateTime nowUtc);

        /// <summary>Records of a product in a time range, newest first, at most <paramref name="limit"/>.</summary>
        IReadOnlyList<DownloadRecord> Read(Guid productId, DateTime fromUtc, DateTime toUtc, int limit);

        /// <summary>Removes records older than the time; returns how many.</summary>
        int RemoveOlderThan(DateTime utc);

        /// <summary>Writes that failed since the process started (a self-monitoring sensor with an alert reads it).</summary>
        long FailedWrites { get; }
    }
}
