using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringCoreInterface
{
    public interface ISensorsInterface
    {
        void RemoveSensorsData(string productId);
        void RemoveSensorData(Guid sensorId);
        void UpdateSensor(SensorUpdate newSensor);

        List<SensorHistoryData> GetSensorHistory(string product, string path, int n);
        List<SensorHistoryData> GetSensorHistory(string product, string path, DateTime from, DateTime to);
        List<SensorHistoryData> GetAllSensorHistory(string product, string path);

        (byte[] content, string extension) GetFileSensorValueData(string product, string path);
    }
}