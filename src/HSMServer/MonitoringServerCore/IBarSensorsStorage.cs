using System;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Model;

namespace HSMServer.MonitoringServerCore
{
    internal interface IBarSensorsStorage : IDisposable
    {
        void Add(IntBarSensorValue value, string product, DateTime timeCollected);
        void Add(DoubleBarSensorValue value, string product, DateTime timeCollected);
        void Remove(string product, string path);
        ExtendedBarSensorData GetLastValue(string product, string path);
        event EventHandler<ExtendedBarSensorData> IncompleteBarOutdated;
    }
} 