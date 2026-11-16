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
    }

    /// <summary>
    /// A built-in sender that repeats a failed request inside a send. The stop drain must not
    /// wait for those repeats (#1747): against a server that is down they spend the whole stop
    /// budget and deliver nothing. The queues therefore drain through <see cref="StopDrain"/>,
    /// where a send is a single attempt, as every send of the native collector is.
    ///
    /// A view and not a mode of the sender: a sender can be shared by two collectors, and the
    /// stop of one must not change the sends of the other.
    /// </summary>
    internal interface IStopDrainSender
    {
        /// <summary>The same sender with one attempt per send. Not disposed by its user.</summary>
        IDataSender StopDrain { get; }
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
