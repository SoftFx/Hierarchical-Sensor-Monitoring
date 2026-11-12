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
        internal PackageSendFailedException(string message) : base(message) { }
    }
}
