using System;
using System.Collections.Generic;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Model.Sensor;

namespace HSMServer.Core.MonitoringCoreInterface
{
    public interface ISensorsInterface
    {
        void RemoveSensor(string product, string key, string path);
        void RemoveSensors(string product, string key, IEnumerable<string> paths);
        bool HideProduct(Product product, out string error);
        List<SensorData> GetSensorUpdates(User user);
        List<SensorData> GetSensorsTree(User user);
        List<SensorHistoryData> GetSensorHistory(User user, string product, string path, int n);
        List<SensorHistoryData> GetSensorHistory(User user, string product, string path, DateTime from, DateTime to);
        List<SensorHistoryData> GetAllSensorHistory(User user, string product, string path);
        string GetFileSensorValue(User user, string product, string path);
        byte[] GetFileSensorValueBytes(User user, string product, string path);
        string GetFileSensorValueExtension(User user, string product, string path);
    }
}