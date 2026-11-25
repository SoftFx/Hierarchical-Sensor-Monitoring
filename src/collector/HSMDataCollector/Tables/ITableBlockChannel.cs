using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMSensorDataObjects.SensorRequests;
using HSMTableBlocks;

namespace HSMDataCollector.Tables
{
    /// <summary>
    /// What a sender has to offer for table sensors: their registration on the session and the delivery of blocks
    /// (ADR-0021 points 5–7). Internal: the public <c>IDataSender</c> is implemented by hosts and cannot grow. The
    /// collector session sender implements it, and so do the test senders.
    /// </summary>
    internal interface ITableBlockChannel
    {
        /// <summary>Why this sender cannot carry tables on this host, or null when it can.</summary>
        string TablesUnavailableReason { get; }

        /// <summary>The largest block (the 40-byte frame header not included) one frame may carry now.</summary>
        int MaxBlockBytes { get; }

        /// <summary>The limits a block is checked against by the server.</summary>
        TableBlockLimits BlockLimits { get; }

        /// <summary>Completes when a registration could be sent: at once when the transport is up now.</summary>
        Task WaitUntilReadyAsync(CancellationToken token);

        /// <summary>
        /// Sends the registrations of table sensors as one request of their own (never mixed with other sensors), and
        /// says for each path whether it was accepted, refused for good, or is still unsettled. Does not throw for a
        /// failure of the transport; throws <see cref="OperationCanceledException"/> when cancelled.
        /// </summary>
        Task<TableRegistrationResult> RegisterTablesAsync(IReadOnlyList<AddOrUpdateSensorRequest> registrations, CancellationToken token);

        /// <summary>
        /// Puts one block under the producer's id and sequence and returns the server's answer. Does not throw for a
        /// failure of the transport (that is <see cref="TableBlockOutcome.NotDelivered"/>); throws
        /// <see cref="OperationCanceledException"/> when cancelled.
        /// </summary>
        Task<TableBlockAck> PutBlockAsync(Guid producer, long sequence, byte[] block, CancellationToken token);

        /// <summary>
        /// Closes the transport of the blocks at the end of a stop's table drain, within <paramref name="budget"/>; the
        /// next Start of the collector opens it again. Does not throw.
        /// </summary>
        Task CloseAsync(CancellationToken budget);
    }


    /// <summary>The answer to a registration request of table sensors, by path.</summary>
    internal sealed class TableRegistrationResult
    {
        internal TableRegistrationResult(IEnumerable<string> accepted, IReadOnlyDictionary<string, string> refused, string unsettledReason)
        {
            Accepted = new HashSet<string>(accepted ?? Array.Empty<string>(), StringComparer.Ordinal);
            Refused = refused ?? new Dictionary<string, string>();
            UnsettledReason = unsettledReason;
        }

        /// <summary>Paths the server took.</summary>
        internal ISet<string> Accepted { get; }

        /// <summary>Paths the server refused for good, with its words. A repeat would be refused again.</summary>
        internal IReadOnlyDictionary<string, string> Refused { get; }

        /// <summary>Why the paths in neither set are unsettled (no session, no answer, a fault); null when all are settled.</summary>
        internal string UnsettledReason { get; }

        internal static TableRegistrationResult NotSent(string why) =>
            new TableRegistrationResult(null, null, why);
    }


    internal enum TableBlockOutcome : byte
    {
        /// <summary>No answer, or a fault of the server (503, 500): keep the block and send it again.</summary>
        NotDelivered = 0,

        /// <summary>Stored; the tables listed as refused were not.</summary>
        Stored,

        /// <summary>This producer's block with this sequence was stored before; settled.</summary>
        Duplicate,

        /// <summary>Every table was refused and nothing was stored. Final.</summary>
        AllRefused,

        /// <summary>A 400: the block breaks the format or the limits. Final.</summary>
        Invalid,

        /// <summary>
        /// Not sent on the connection that judged it: the block is larger than a frame of the table connection takes now,
        /// or breaks the block limits of its hello (a server whose hello states smaller numbers than the ones the block
        /// was packed for). The pipeline packs its rows again into smaller blocks; a block of one row cannot be made
        /// smaller, so for it the answer is final (dropped and counted as too large).
        /// </summary>
        TooLarge,
    }


    internal readonly struct TableRefusal
    {
        internal TableRefusal(int index, string table, string reason)
        {
            Index = index;
            Table = table;
            Reason = reason;
        }

        /// <summary>The table's index in the block's directory.</summary>
        internal int Index { get; }

        internal string Table { get; }

        internal string Reason { get; }
    }


    /// <summary>The server's answer to one block.</summary>
    internal sealed class TableBlockAck
    {
        private static readonly IReadOnlyList<TableRefusal> NoRefusals = Array.Empty<TableRefusal>();

        private TableBlockAck(TableBlockOutcome outcome, IReadOnlyList<TableRefusal> refused, int refusedCount, string error, TimeSpan? retryAfter)
        {
            Outcome = outcome;
            Refused = refused ?? NoRefusals;
            RefusedCount = Math.Max(refusedCount, Refused.Count);
            Error = error;
            RetryAfter = retryAfter;
        }

        internal TableBlockOutcome Outcome { get; }

        /// <summary>The refused tables the answer lists (at most the first 100 of them).</summary>
        internal IReadOnlyList<TableRefusal> Refused { get; }

        /// <summary>The exact number of refused tables, which may exceed the listed ones.</summary>
        internal int RefusedCount { get; }

        /// <summary>The server's text, or why there was no answer.</summary>
        internal string Error { get; }

        /// <summary>How long to wait before the next attempt, when the server said so.</summary>
        internal TimeSpan? RetryAfter { get; }

        internal static TableBlockAck Stored(IReadOnlyList<TableRefusal> refused = null, int refusedCount = 0) =>
            new TableBlockAck(TableBlockOutcome.Stored, refused, refusedCount, null, null);

        internal static TableBlockAck Duplicate(IReadOnlyList<TableRefusal> refused = null, int refusedCount = 0) =>
            new TableBlockAck(TableBlockOutcome.Duplicate, refused, refusedCount, null, null);

        internal static TableBlockAck AllRefused(IReadOnlyList<TableRefusal> refused, int refusedCount) =>
            new TableBlockAck(TableBlockOutcome.AllRefused, refused, refusedCount, null, null);

        internal static TableBlockAck Invalid(string error) =>
            new TableBlockAck(TableBlockOutcome.Invalid, null, 0, error, null);

        internal static TableBlockAck NotDelivered(string why, TimeSpan? retryAfter = null) =>
            new TableBlockAck(TableBlockOutcome.NotDelivered, null, 0, why, retryAfter);

        internal static TableBlockAck TooLarge(string why) =>
            new TableBlockAck(TableBlockOutcome.TooLarge, null, 0, why, null);
    }
}
