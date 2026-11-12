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
    /// A built-in sender that reports its own faults (a failed attempt it repeats by itself, a
    /// connection it lost). The collector that created the sender passes its deduplicated error
    /// channel; until then, and for a sender used on its own, the reports go to the logger.
    /// </summary>
    internal interface IErrorReportingSender
    {
        void RouteErrorsTo(Action<string> sink);
    }
}
