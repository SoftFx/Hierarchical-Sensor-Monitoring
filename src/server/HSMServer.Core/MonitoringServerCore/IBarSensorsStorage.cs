using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringServerCore
{
    public interface IBarSensorsStorage : IDisposable
    {
        void Add<T>(T value, string product, DateTime timeCollected) where T : BarSensorValueBase;
        void Remove(string product, string path);
        ExtendedBarSensorData GetLastValue(string product, string path);
        List<ExtendedBarSensorData> GetAllLastValues();
        event EventHandler<ExtendedBarSensorData> IncompleteBarOutdated;
    }
}