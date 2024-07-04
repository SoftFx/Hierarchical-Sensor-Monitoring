using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace DataCollectorSandboxCore
{
    internal class DataSender : IDataSender
    {

        public void Dispose()
        {

        }

        public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
        {
            return default;
        }

        public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            return default;
        }

        public ValueTask<PackageSendingInfo> SendFileAsync(FileSensorValue file, CancellationToken token)
        {
            return default;
        }

        public ValueTask<PackageSendingInfo> SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            return default;
        }

        public ValueTask<ConnectionResult> TestConnectionAsync()
        {
            return default;
        }
    }
}
