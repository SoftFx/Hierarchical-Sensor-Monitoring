using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Core
{
    public interface IDataSender
    {
        ValueTask SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token);

        ValueTask SendPriorityDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token);

        ValueTask SendCommandAsync(IEnumerable<CommandRequestBase> commands, CancellationToken token);

        ValueTask SendFileAsync(FileSensorValue file, CancellationToken token);

        event Action<PackageSendingInfo> OnSendPackage;
    }
}
