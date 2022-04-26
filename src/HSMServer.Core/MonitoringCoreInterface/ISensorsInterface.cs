using System;
using System.Collections.Generic;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Cache.Entities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.MonitoringCoreInterface
{
    public interface ISensorsInterface
    {
        void AddSensor(string productName, SensorValueBase sensorValue);
        void RemoveSensorsData(string productId);
        void RemoveSensorData(Guid sensorId);
        void RemoveSensor(string productName, string path);
        void RemoveSensor(string product, string key, string path);
        void RemoveSensors(string product, string key, IEnumerable<string> paths);
        void UpdateSensorInfo(SensorInfo newInfo);
        void UpdateSensor(UpdatedSensor newSensor);
        bool IsSensorRegistered(string productName, string path);
        SensorInfo GetSensorInfo(string productName, string path);
        List<SensorInfo> GetProductSensors(string productName);

        List<SensorHistoryData> GetSensorHistory(string product, string path, int n);
        List<SensorHistoryData> GetSensorHistory(string product, string path, DateTime from, DateTime to);
        List<SensorHistoryData> GetAllSensorHistory(string product, string path);

        (byte[] content, string extension) GetFileSensorValueData(string product, string path);
    }
}