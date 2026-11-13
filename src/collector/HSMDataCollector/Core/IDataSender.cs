using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Core
{
    public interface IDataSender : IDisposable
    {
        ValueTask<ConnectionResult> TestConnectionAsync();

        ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token);

        ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token);

        ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token);

        ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token);
    }

    internal interface ICancelableDataSender
    {
        void CancelPendingRequests();
    }

    /// <summary>
    /// A built-in sender that holds a connection of its own, which the collector opens at Start
    /// and closes when a stop has drained the queues (the collector session).
    /// </summary>
    internal interface ICollectorTransport
    {
        /// <summary>Called by Start. Returns at once and does not throw.</summary>
        void Start();

        /// <summary>Called after the stop drain; <paramref name="terminal"/> for a Dispose. Bounded; does not throw.</summary>
        System.Threading.Tasks.Task StopAsync(bool terminal);

        /// <summary>
        /// Completes when the transport has a connection: at once when it has one now. A queue
        /// whose package could not be sent for want of a connection waits here, so that the
        /// package leaves when the connection is there and not a collect period later.
        /// </summary>
        System.Threading.Tasks.Task WaitForConnectionAsync(System.Threading.CancellationToken token);

        /// <summary>
        /// False when the transport has no way to carry a file value. The collector then refuses
        /// a file value before it is queued: queued, it would be sent, refused and re-queued for ever.
        /// </summary>
        bool CanCarryFiles { get; }
    }

    /// <summary>
    /// A built-in sender that reports its own faults (a failed attempt it repeats by itself, a
    /// connection it lost). The collector that created the sender passes its deduplicated error
    /// channel; until then, and for a sender used on its own, the reports go to the logger.
    /// </summary>
    internal interface IErrorReportingSender
    {
        void RouteErrorsTo(Action<string> sink);
    }
}
