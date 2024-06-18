using System;
using System.Collections.Generic;
using HSMDataCollector.SyncQueue;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;


namespace HSMDataCollector.Core
{
    internal interface IQueueManager
    {
        void Init();

        void Stop();

        void AddData(SensorValueBase item);

        void AddData(IEnumerable<SensorValueBase> items);

        void AddPriorityData(SensorValueBase item);

        void AddPriorityData(IEnumerable<SensorValueBase> items);

        void AddCommand(CommandRequestBase command);

        void AddCommand(IEnumerable<CommandRequestBase> commands);

        void AddFile(FileSensorValue file);

        void AddException(string SensorPath, Exception ex);

    }
}
