using System;

namespace HSMDataCollector.PublicInterface
{
    /// <summary>What became of a large file handed over by <see cref="Core.ILargeFileCollector.SendLargeFileAsync"/>.</summary>
    public enum LargeFileOutcome : byte
    {
        /// <summary>The server stored the whole file and checked it; it is a value of the sensor now. The source was deleted
        /// unless <c>DeleteAfterUpload</c> is false (see <see cref="LargeFileResult.SourceDeleted"/>).</summary>
        Stored = 0,

        /// <summary>A final refusal: the file is larger than the server takes, the sensor cannot take it, or the request was
        /// malformed. The file was left where it is and is not offered again before the collector's next Start.</summary>
        Refused = 1,

        /// <summary>The collector gave up after failures that a repeat does not cure (the server discarded the upload three
        /// times, too many checksum mismatches, repeated protocol errors). The file was left where it is and is offered again
        /// only when it changes.</summary>
        GivenUp = 2,

        /// <summary>The file was not there, was a symbolic link or a reparse point, or could not be opened for reading (access
        /// denied, an error of the system).</summary>
        Missing = 3,

        /// <summary>This collector cannot send large files (the HTTP transport, a custom <c>DataSender</c>, the session of
        /// another collector). <see cref="LargeFileResult.Reason"/> says why. Nothing was sent.</summary>
        Unavailable = 4,

        /// <summary>The token fired or the collector was disposed before the file was settled. An upload in progress was cut;
        /// its part stays on the server for a continue when the file is handed over again.</summary>
        Cancelled = 5,
    }


    /// <summary>The result of one large file handed over to the collector.</summary>
    public sealed class LargeFileResult
    {
        internal LargeFileResult(LargeFileOutcome outcome, string reason, DateTime? storedTime = null, bool sourceDeleted = false)
        {
            Outcome = outcome;
            Reason = reason;
            StoredTime = storedTime;
            SourceDeleted = sourceDeleted;
        }

        /// <summary>What became of the file.</summary>
        public LargeFileOutcome Outcome { get; }

        /// <summary>Why the file was not stored, in words for a log; null for <see cref="LargeFileOutcome.Stored"/>, or the reason the
        /// stored source was not deleted.</summary>
        public string Reason { get; }

        /// <summary>The time of the value the server made of the file (UTC), when it said it; null otherwise.</summary>
        public DateTime? StoredTime { get; }

        /// <summary>True when the collector deleted the source file after the server stored it.</summary>
        public bool SourceDeleted { get; }

        public override string ToString() => Reason == null ? Outcome.ToString() : $"{Outcome}: {Reason}";
    }
}
