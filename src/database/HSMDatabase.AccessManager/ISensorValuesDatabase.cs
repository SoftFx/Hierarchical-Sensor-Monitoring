using HSMDatabase.AccessManager.DatabaseEntities;

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
    }
}
