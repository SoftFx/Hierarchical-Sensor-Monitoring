﻿using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager
{
    public interface ISensorValuesDatabase : IDisposable
    {
        long From { get; }

        long To { get; }


        //void OpenDatabase(string dbPath);

        //void DisposeDatabase(string sensorId);

        //void RemoveDatabase(string sensorId);

        void FillLatestValues(Dictionary<byte[], (Guid sensorId, byte[] latestValue)> keyValuePairs);

        void PutSensorValue(SensorValueEntity entity);

        void PutSensorValue(string sensorId, string time, byte[] value);

        void RemoveSensorValues(string sensorId);

        //bool IsDatabaseExists(string sensorId);

        //byte[] GetLatestValue(string sensorId);

        List<byte[]> GetValues(string sensorId, byte[] to, int count);

        List<byte[]> GetValues(string sensorId, byte[] from, byte[] to);
    }
}
