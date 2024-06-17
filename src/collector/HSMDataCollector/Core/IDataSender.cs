using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HSMDataCollector.Core
{
    public interface IDataSender
    {
        Task SendDataAsync(SensorValueBase data, CancellationToken token);

        Task SendDataAsync(IEnumerable<SensorValueBase> items, CancellationToken token);

        Task<string> SendCommandAsync(CommandRequestBase command, CancellationToken token);

        Task<Dictionary<string, string>> SendCommandAsync(IEnumerable<CommandRequestBase> command, CancellationToken token);

        Task SendFileAsync(FileSensorValue file, CancellationToken token);
    }
}
