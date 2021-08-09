using System;
using System.Collections.Generic;
using HSMDatabase.Entity;

namespace HSMDatabase.SensorsDatabase
{
    internal interface ISensorsDatabase
    {
        long DatabaseMinTicks { get; }
        long DatabaseMaxTicks { get; }
        DateTime DatabaseMaxDateTime { get; }
        DateTime DatabaseMinDateTime { get; }
        long GetSensorSize(string productName, string path);
        void PutSensorData(SensorDataEntity sensorData, string productName);
        SensorDataEntity GetLatestSensorValue(string productName, string path);
        List<SensorDataEntity> GetAllSensorValues(string productName, string path);
        //List<SensorDataEntity> GetSensorValues(string productName, string path, int count);
        List<SensorDataEntity> GetSensorValuesFrom(string productName, string path, DateTime from);
        List<SensorDataEntity> GetSensorValuesBetween(string productName, string path, DateTime from, DateTime to);
    }
}