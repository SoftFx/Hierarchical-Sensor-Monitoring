using System;
using System.Collections.Generic;
using HSMSensorDataObjects.FullDataObject;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.MonitoringCoreInterface
{
    public interface ISensorsInterface
    {
        void AddSensor(string productName, SensorValueBase sensorValue);
        void RemoveSensor(string productName, string path);
        void RemoveSensor(string product, string key, string path);
        void RemoveSensors(string product, string key, IEnumerable<string> paths);
        void UpdateSensorInfo(SensorInfo newInfo);
        bool IsSensorRegistered(string productName, string path);
        SensorInfo GetSensorInfo(string productName, string path);
        List<SensorInfo> GetProductSensors(string productName);

        List<SensorHistoryData> GetSensorHistory(string product, string path, int n);
        List<SensorHistoryData> GetSensorHistory(string product, string path, DateTime from, DateTime to);
        List<SensorHistoryData> GetAllSensorHistory(string product, string path);

        byte[] GetFileSensorValueBytes(string product, string path);
        string GetFileSensorValueExtension(string product, string path);

        //ToDo: move
        bool HideProduct(Product product, out string error);

        List<SensorData> GetSensorUpdates(User user);
        List<SensorData> GetSensorsTree(User user);
    }
}