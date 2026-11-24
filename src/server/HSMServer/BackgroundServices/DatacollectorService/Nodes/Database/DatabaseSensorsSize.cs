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
        internal const string JournalsDbName = "Journals";
        internal const string HistoryDbName = "History";
        internal const string ConfigDbName = "Config";
        internal const string TotalDbName = "Total";
        internal const string EnvironmentDbName = "Environment";
        internal const string DashboardsDbName = "Dashboards";
        internal const string TablesDbName = "Tables";

        private const string TablesDescription = "The block store of table sensors (Tables: Blocks and BlockIndex). " +
            "It is NOT included in the database backups, like sensor history. The sensor appears once the store exists.";

        // Created when the block store's folder first exists (#1844): a server without table sensors gets no such sensor.
        private DatabaseSizeSensor _tables;


        private readonly Dictionary<string, DatabaseSizeSensor> Sensors = new()
        {
            { ConfigDbName, new($"The sensor displays sum of the EnvironmentData, ServerLayout and Snapshots database sizes.  \n" +
                                "* EnvironmentData - the database contains all server entities meta information (folders, products, sensors, users and etc.)  \n" +
                                "* ServerLayout - the database contains information about dashboards (meta information, information about charts, panels and etc.)  \n" +
                                "* Snapshots - the database contains current state of tree (last update time of sensors, timeouts and etc.)") },
            { HistoryDbName, new("The database (SensorValues) contains sensors history divided into weekly folders. " +
                                 "It is NOT included in the database backups: " + BackupSensors.NotBackedUpNote) },
            { EnvironmentDbName, new("The EnvironmentData database contains all server entities meta information (folders, products, sensors, users and etc.). " +
                                     "It is backed up on the backup schedule (see the Backup node).") },
            { DashboardsDbName, new("The ServerLayout database contains information about dashboards (meta information, information about charts, panels and etc.). " +
                                    "It is backed up on the backup schedule (see the Backup node).") },
            { JournalsDbName, new("The database contains journal records for each sensor.") },
            { TotalDbName, new("All database size is the sum of the sizes of Environment, SensorValues, Snapshots, ServerLayout, Journals and Tables databases.") },
        };


        internal DatabaseSensorsSize(IDataCollector collector, IDatabaseCore database, IServerConfig config)
            : base(collector, database, config)
        {
            CreateDataSizeSensor(HistoryDbName, () => _database.SensorHistoryDbSize);
            CreateDataSizeSensor(JournalsDbName, () => _database.JournalDbSize);
            CreateDataSizeSensor(ConfigDbName, () => _database.ConfigDbSize);
            CreateDataSizeSensor(TotalDbName, () => _database.TotalDbSize);
            CreateDataSizeSensor(EnvironmentDbName, () => _database.EnvironmentDbSize);
            CreateDataSizeSensor(DashboardsDbName, () => _database.ServerLayoutDbSize);
        }


        internal override void SendInfo()
        {
            foreach (var (_, sensor) in Sensors)
                sensor.SendInfo();

            if (_tables is null && _database.TableBlocks?.Exists == true)
            {
                _tables = new DatabaseSizeSensor(TablesDescription) { GetSize = () => _database.TableBlocksDbSize };
                _tables.Sensor = _collector.CreateDoubleSensor($"{NodeName}/{TablesDbName} data size", SizeOptions(TablesDbName, TablesDescription));
            }

            _tables?.SendInfo();
        }

        private void CreateDataSizeSensor(string databaseName, Func<long> getSizeFunc)
        {
            var sensor = Sensors[databaseName];

            sensor.GetSize = getSizeFunc;
            sensor.Sensor = _collector.CreateDoubleSensor($"{NodeName}/{databaseName} data size", SizeOptions(databaseName, sensor.Description));
        }

        private static InstantSensorOptions SizeOptions(string databaseName, string description) => new()
        {
            Alerts = [],
            TTL = TimeSpan.MaxValue,
            EnableForGrafana = true,
            SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.MB,
            KeepHistory = TimeSpan.MaxValue,
            Description = $"The sensor sends information about {databaseName} database size in MB, posted once a day. {description}"
        };


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
