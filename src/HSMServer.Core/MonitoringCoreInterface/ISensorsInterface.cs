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

        void UpdateSensorInfo(SensorInfo newInfo);

        void RemoveSensor(string productName, string path);
        void RemoveSensors(string product, string key, IEnumerable<string> paths);

        bool IsSensorRegistered(string productName, string path);

        SensorInfo GetSensorInfo(string productName, string path);
        ICollection<SensorInfo> GetProductSensors(string productName);

        List<SensorData> GetSensorUpdates(User user);
        List<SensorData> GetSensorsTree(User user);
        List<SensorHistoryData> GetSensorHistory(User user, string product, string path, int n);
        List<SensorHistoryData> GetSensorHistory(User user, string product, string path, DateTime from, DateTime to);
        List<SensorHistoryData> GetAllSensorHistory(User user, string product, string path);

        byte[] GetFileSensorValueBytes(User user, string product, string path);
        string GetFileSensorValueExtension(User user, string product, string path);

        //ToDo: move
        bool HideProduct(Product product, out string error);
    }
}