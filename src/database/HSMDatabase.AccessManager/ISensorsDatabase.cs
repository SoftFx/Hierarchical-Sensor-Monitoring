using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager
{
    public interface ISensorsDatabase : IDisposable
    {
        long DatabaseMinTicks { get; }
        long DatabaseMaxTicks { get; }
        DateTime DatabaseMaxDateTime { get; }
        DateTime DatabaseMinDateTime { get; }
        long GetSensorSize(string productName, string path);
        void PutSensorData(SensorDataEntity sensorData, string productName);
        void DeleteAllSensorValues(string productName, string path);
        SensorDataEntity GetLatestSensorValue(string productName, string path);
        List<SensorDataEntity> GetAllSensorValues(string productName, string path);
        List<byte[]> GetAllSensorValuesBytes(string productName, string path);
        List<byte[]> GetSensorValues(string productName, string path, DateTime to, int count);
        List<SensorDataEntity> GetSensorValuesFrom(string productName, string path, DateTime from);
        List<SensorDataEntity> GetSensorValuesBetween(string productName, string path, DateTime from, DateTime to);

        void FillLatestValues(Dictionary<byte[], (Guid sensorId, byte[] latestValue)> keyValuePairs);
    }
}
