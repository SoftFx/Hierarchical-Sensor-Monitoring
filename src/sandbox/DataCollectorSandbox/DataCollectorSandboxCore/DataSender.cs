using HSMDataCollector.Core;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using Newtonsoft.Json;
using System.Text.Json;


namespace DataCollectorSandboxCore
{
    internal class DataSender : IDataSender
    {
        public event Action<PackageSendingInfo>? OnSendPackage;

        private static JsonSerializerOptions _options = new JsonSerializerOptions
        {
            Converters = { new VersionConverter() }
        };

        public void Dispose()
        {

        }

        public ValueTask<PackageSendingInfo> SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token)
        {
            return default;
        }

        public ValueTask<PackageSendingInfo> SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token)
        {
            var res = items.ToList();

            var content = JsonConvert.SerializeObject(res);
            var content1 = System.Text.Json.JsonSerializer.Serialize(res, _options);


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
