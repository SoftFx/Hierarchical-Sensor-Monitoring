using System.Collections.Generic;
using HSMCommon.Model.SensorsData;

namespace HSMServer.Cache
{
    public interface IValuesCache
    {
        void AddValue(string productName, SensorData sensorData);
        List<SensorData> GetValues(List<string> products);
        void RemoveSensorValue(string productName, string path);
        void RemoveProduct(string productName);
        SensorData GetValue(string productName, string path);
    }
}