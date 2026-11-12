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
        internal PackageSendFailedException(string message, bool isTransportDown = false) : base(message)
        {
            IsTransportDown = isTransportDown;
        }

        /// <summary>
        /// The package was not sent because the transport has no connection, which the transport
        /// reports itself. The queue keeps the package; a line per cycle would only repeat it.
        /// </summary>
        internal bool IsTransportDown { get; }
    }
}
