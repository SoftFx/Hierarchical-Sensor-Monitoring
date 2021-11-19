using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;

namespace HSMDatabase.AccessManager
{
    public interface ISensorsDatabase
    {
        long DatabaseMinTicks { get; }
        long DatabaseMaxTicks { get; }
        DateTime DatabaseMaxDateTime { get; }
        DateTime DatabaseMinDateTime { get; }
        long GetSensorSize(string productName, string path);
        void PutSensorData(ISensorDataEntity sensorData, string productName);
        void DeleteAllSensorValues(string productName, string path);
        ISensorDataEntity GetLatestSensorValue(string productName, string path);
        List<ISensorDataEntity> GetAllSensorValues(string productName, string path);
        List<ISensorDataEntity> GetSensorValuesFrom(string productName, string path, DateTime from);
        List<ISensorDataEntity> GetSensorValuesBetween(string productName, string path, DateTime from, DateTime to);
    }
}
