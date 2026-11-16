using System;

namespace HSMDataCollector.SyncQueue.Data
{
    /// <summary>
    /// A queue processor's report that the sender failed a package and what became of its items.
    /// The message is the whole report; the stack trace only says where the queue threw it.
    /// Derives from <see cref="InvalidOperationException"/>, which this report used to be.
    /// </summary>
    internal sealed class PackageSendFailedException : InvalidOperationException
    {
        /// <summary>
        /// The report of the failure without the item counts: what happened to the queue and
        /// why. The counts differ from cycle to cycle while a failure lasts (the package grows
        /// with what queued up meanwhile), so a report keyed with them was a new report on
        /// every cycle (#1659 A4).
        /// </summary>
        internal string Report { get; }

        /// <summary>
        /// The package was not sent because the transport has no connection, which the transport
        /// reports itself. The queue keeps the package; a line per cycle would only repeat it.
        /// </summary>
        internal bool IsTransportDown { get; }

        internal PackageSendFailedException(string queueName, int preserved, int dropped, bool flushing, string error, bool isTransportDown = false)
            : base($"Failed to send package for {queueName} ({preserved} {Fate(flushing)}{(dropped > 0 ? $", {dropped} dropped" : string.Empty)}). {error}")
        {
            Report = $"Failed to send package for {queueName} (items {Fate(flushing)}{(dropped > 0 ? ", some dropped at capacity" : string.Empty)}). {error}";
            IsTransportDown = isTransportDown;
        }

        // During FlushAsync the items are re-enqueued only so ClearQueue can log them as
        // "discarded"; calling them "preserved" contradicts the very next log line.
        private static string Fate(bool flushing) => flushing ? "queued for clear" : "preserved";
    }
}
