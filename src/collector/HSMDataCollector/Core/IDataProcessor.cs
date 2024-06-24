using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMDataCollector.SyncQueue.Data;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Core
{
    internal interface IDataProcessor
    {
        Task InitAsync();

        Task StopAsync();

        void AddData(SensorValueBase item);

        void AddData(IEnumerable<SensorValueBase> items);

        void AddPriorityData(SensorValueBase item);

        void AddPriorityData(IEnumerable<SensorValueBase> items);

        void AddCommand(CommandRequestBase command);

        void AddCommand(IEnumerable<CommandRequestBase> commands);

        void AddFile(FileSensorValue file);

        void AddException(string SensorPath, Exception ex);

        void AddPackageInfo(string name, PackageInfo info);

        void AddPackageSendingInfo(PackageSendingInfo info);
    }
}
