using System;
using System.Threading;
using System.Threading.Tasks;

namespace HSMServer.Core.LargeFiles.Archives
{
    /// <summary>Why a verified (or re-encrypted) file has no archive under the current password yet.</summary>
    public enum ArchiveBuildState : byte
    {
        /// <summary>Nothing pending: packed under the current password, or not a file the builder knows.</summary>
        None = 0,

        /// <summary>The product has no dump password: the file is kept verified and cannot be downloaded.</summary>
        WaitingForPassword,

        /// <summary>Queued for its first build or its re-encryption.</summary>
        Queued,

        Building,

        /// <summary>The store's volume has no room for the archive now; tried again later, with an alert.</summary>
        WaitingForRoom,

        /// <summary>The last attempt failed; the part (or the old archive) is kept and the build is tried again, with an alert.</summary>
        Failed,
    }


    public sealed record ArchiveBuildStatus(ArchiveBuildState State, int Attempts, DateTime? NextAttemptUtc, string LastError);


    /// <summary>Counters since the process started, for the self-monitoring sensors.</summary>
    public sealed class ArchiveBuildStatistics
    {
        public long Built;
        public long Reencrypted;
        public long Failed;
        public long WaitedForRoom;
        public long PasswordsRetired;
    }


    /// <summary>
    /// Builds the archive of every verified file and re-encrypts the archives of a product whose password changed, one at a
    /// time for the whole server (ADR-0022 point 13). The cleanup of password versions runs in the same worker.
    /// </summary>
    public interface IArchiveBuilder
    {
        ArchiveBuildStatus GetStatus(Guid fileId);

        ArchiveBuildStatistics Statistics { get; }

        /// <summary>Raised after an attempt ends: (file id, ok, error text without any password).</summary>
        event Action<Guid, bool, string> BuildFinished;

        /// <summary>Does every piece of work that is due now, one file after another; returns when nothing is due.</summary>
        Task RunDueAsync(CancellationToken ct);
    }
}
