using HSMDatabase.AccessManager.DatabaseEntities;
using System.Collections.Generic;

namespace HSMDatabase.AccessManager
{
    public interface ISensorValuesDatabase
    {
        long From { get; }

        long To { get; }


        void OpenDatabase(string dbPath);

        void PutSensorValue(SensorValueEntity entity);

        bool IsDatabaseExists(string sensorId);

        void DisposeDatabase(string sensorId);

        byte[] GetLatestValue(string sensorId);

        List<byte[]> GetValues(string sensorId, byte[] to, int count);

        List<byte[]> GetValues(string sensorId, byte[] from, byte[] to);
    }
}
