using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMServer.Core.DataLayer;
using HSMServer.ServerConfiguration;
using System;
using System.Collections.Generic;

namespace HSMServer.BackgroundServices
{
    public sealed class DatabaseSensorsSize : DatabaseSensorsBase
    {
        private const string JournalsDbName = "Journals";
        private const string HistoryDbName = "History";
        private const string ConfigDbName = "Config";
        private const string TotalDbName = "Total";


        private readonly Dictionary<string, DatabaseSizeSensor> Sensors = new()
        {
            { ConfigDbName, new($"The sensor displays sum of the EnvironmentData, ServerLayout and Snapshots database sizes.  \n" +
                                "* EnvironmentData - the database contains all server entities meta information (folders, products, sensors, users and etc.)  \n" +
                                "* ServerLayout - the database contains information about dashboards (meta information, information about charts, panels and etc.)  \n" +
                                "* Snapshots - the database contains current state of tree (last update time of sensors, timeouts and etc.)") },
            { HistoryDbName, new("The database contains sensors history divided into weekly folders.") },
            { JournalsDbName, new("The database contains journal records for each sensor.") },
            { TotalDbName, new("All database size is the sum of the sizes of Environment, SensorValues, Snapshots, ServerLayout and Journals databases.") },
        };


        internal DatabaseSensorsSize(IDataCollector collector, IDatabaseCore database, IServerConfig config)
            : base(collector, database, config)
        {
            CreateDataSizeSensor(HistoryDbName, () => _database.SensorHistoryDbSize);
            CreateDataSizeSensor(JournalsDbName, () => _database.JournalDbSize);
            CreateDataSizeSensor(ConfigDbName, () => _database.ConfigDbSize);
            CreateDataSizeSensor(TotalDbName, () => _database.TotalDbSize);
        }


        internal override void SendInfo()
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
                KeepHistory = TimeSpan.MaxValue,
                Description = $"The sensor sends information about {databaseName} database size. {sensor.Description}"
            };

            sensor.GetSize = getSizeFunc;
            sensor.Sensor = _collector.CreateDoubleSensor($"{NodeName}/{databaseName} data size", options);
        }


        private sealed record DatabaseSizeSensor
        {
            internal string Description { get; init; }


            internal IInstantValueSensor<double> Sensor { get; set; }

            internal Func<long> GetSize { get; set; }


            internal DatabaseSizeSensor(string description) => Description = description;


            internal void SendInfo() => Sensor.AddValue(GetRoundedDouble(GetSize()));
        }
    }
}
