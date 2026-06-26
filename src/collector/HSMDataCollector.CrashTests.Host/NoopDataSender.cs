using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.CrashTests.Host
{
    /// <summary>
    /// Accepts and discards everything — the crash scenarios exercise callback isolation, not transport.
    /// </summary>
    internal sealed class NoopDataSender : IDataSender
    {
        public void Dispose()
        {
        }

        public ValueTask<ConnectionResult> TestConnectionAsync() =>
            new ValueTask<ConnectionResult>(ConnectionResult.Ok);

        public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
            default;

        public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token) =>
            default;

        public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token) =>
            default;

        public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token) =>
            default;
    }
}
