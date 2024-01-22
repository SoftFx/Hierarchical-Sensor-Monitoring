using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMServer.Core.DataLayer;
using System;
using System.Collections.Generic;

namespace HSMServer.BackgroundServices
{
    internal sealed class DatabaseSize
    {
        private const double MbDivisor = 1 << 20;
        private const int DigitsCnt = 2;

        private const string EnvironmentDbName = "Environment";
        private const string HistoryDbName = "SensorValues";
        private const string LayoutDbName = "ServerLayout";
        private const string SnaphotsDbName = "Snapshots";
        private const string JournalsDbName = "Journals";
        private const string BackupsDbName = "Backups";
        private const string TotalDbName = "All";


        private readonly IDataCollector _collector;
        private readonly IDatabaseCore _database;


        private readonly Dictionary<string, DatabaseSizeSensor> Sensors = new()
        {
            { EnvironmentDbName, new("The database contains all server entities meta information (folders, products, sensors, users and etc.).") },
            { HistoryDbName, new("The database contains sensors history divided into weekly folders.") },
            { LayoutDbName, new("The database contains information about dashboards (meta information, information about charts, panels and etc.).") },
            { SnaphotsDbName, new("The database contains current state of tree (last update time of sensors, timeouts and etc.).") },
            { JournalsDbName, new("The database contains journal records for each sensor.") },
            { BackupsDbName, new("The database contains backups of Environment and ServerLayout databases.") },
            { TotalDbName, new("All database size is the sum of the sizes of Environment, SensorValues, Snapshots, ServerLayout and Journals databases.") },
        };


        internal DatabaseSize(IDataCollector collector, IDatabaseCore database)
        {
            _collector = collector;
            _database = database;

            CreateDataSizeSensor(EnvironmentDbName, () => _database.EnviromentDbSize);
            CreateDataSizeSensor(SnaphotsDbName, () => _database.SensorHistoryDbSize);
            CreateDataSizeSensor(HistoryDbName, () => _database.ServerLayoutDbSize);
            CreateDataSizeSensor(JournalsDbName, () => _database.Snapshots.Size);
            CreateDataSizeSensor(LayoutDbName, () => _database.JournalDbSize);
            CreateDataSizeSensor(BackupsDbName, () => _database.BackupsSize);
            CreateDataSizeSensor(TotalDbName, () => _database.TotalDbSize);
        }


        internal void SendInfo()
        {
            foreach (var (_, sensor) in Sensors)
                sensor.SendInfo();
        }

        private void CreateDataSizeSensor(string databaseName, Func<long> getSizeFunc)
        {
            var sensor = Sensors[databaseName];
            var options = new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = true,
                SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.MB,

                Description = $"The sensor sends information about {databaseName} database size. {sensor.Description}"
            };

            sensor.GetSize = getSizeFunc;
            sensor.Sensor = _collector.CreateDoubleSensor($"Database/{databaseName} data size", options);
        }


        private sealed record DatabaseSizeSensor
        {
            internal string Description { get; init; }


            internal IInstantValueSensor<double> Sensor { get; set; }

            internal Func<long> GetSize { get; set; }


            internal DatabaseSizeSensor(string description) => Description = description;


            internal void SendInfo()
            {
                static double GetRoundedDouble(long sizeInBytes)
                {
                    return Math.Round(sizeInBytes / MbDivisor, DigitsCnt, MidpointRounding.AwayFromZero);
                }

                Sensor.AddValue(GetRoundedDouble(GetSize()));
            }
        }
    }
}
