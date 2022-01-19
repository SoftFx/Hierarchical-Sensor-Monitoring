using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringServerCore
{
    public interface IBarSensorsStorage : IDisposable
    {
        public void Add<T>(T value, SensorData sensorData) where T : BarSensorValueBase;
        void Remove(string product, string path);
        ExtendedBarSensorData GetLastValue(string product, string path);
        List<ExtendedBarSensorData> GetAllLastValues();
        event EventHandler<ExtendedBarSensorData> IncompleteBarOutdated;
    }
}