using HSMDatabase.AccessManager.DatabaseEntities;
using System;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager
{
    public interface ISensorValuesDatabase : IDisposable
    {
        long From { get; }

        long To { get; }


        void OpenDatabase(string dbPath);

        void DisposeDatabase(string sensorId);

        void RemoveDatabase(string sensorId);

        void PutSensorValue(SensorValueEntity entity);

        bool IsDatabaseExists(string sensorId);

        byte[] GetLatestValue(string sensorId);

        List<byte[]> GetValues(string sensorId, byte[] to, int count);

        List<byte[]> GetValues(string sensorId, byte[] from, byte[] to);
    }
}
