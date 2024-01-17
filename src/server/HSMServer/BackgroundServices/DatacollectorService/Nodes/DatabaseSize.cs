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

        private readonly IInstantValueSensor<double> _environmentDbSizeSensor;
        private readonly IInstantValueSensor<double> _snapshotsDbSizeSensor;
        private readonly IInstantValueSensor<double> _historyDbSizeSensor;
        private readonly IInstantValueSensor<double> _journalDbSizeSensor;
        private readonly IInstantValueSensor<double> _layoutDbSizeSensor;
        private readonly IInstantValueSensor<double> _backupsSizeSensor;
        private readonly IInstantValueSensor<double> _dbSizeSensor;


        private readonly Dictionary<string, string> Descriptions = new()
        {
            { EnvironmentDbName, "The database contains all server entities meta information (folders, products, sensors, users and etc.)." },
            { HistoryDbName, "The database contains sensors history divided into weekly folders." },
            { LayoutDbName, "The database contains information about dashboards (meta information, information about charts, panels and etc.)." },
            { SnaphotsDbName, "The database contains current state of tree (last update time of sensors, timeouts and etc.)." },
            { JournalsDbName, "The database contains journal records for each sensor." },
            { BackupsDbName, "The database contains backups of Environment and ServerLayout databases." },
            { TotalDbName, "All database size is the sum of the sizes of Environment, SensorValues, Snapshots, ServerLayout and Journals databases." },
        };


        internal DatabaseSize(IDataCollector collector, IDatabaseCore database)
        {
            _collector = collector;
            _database = database;

            _environmentDbSizeSensor = CreateDataSizeSensor(EnvironmentDbName);
            _snapshotsDbSizeSensor = CreateDataSizeSensor(SnaphotsDbName);
            _historyDbSizeSensor = CreateDataSizeSensor(HistoryDbName);
            _journalDbSizeSensor = CreateDataSizeSensor(JournalsDbName);
            _layoutDbSizeSensor = CreateDataSizeSensor(LayoutDbName);
            _backupsSizeSensor = CreateDataSizeSensor(BackupsDbName);
            _dbSizeSensor = CreateDataSizeSensor(TotalDbName);
        }


        internal void SendInfo()
        {
            static double GetRoundedDouble(long sizeInBytes)
            {
                return Math.Round(sizeInBytes / MbDivisor, DigitsCnt, MidpointRounding.AwayFromZero);
            }

            _environmentDbSizeSensor.AddValue(GetRoundedDouble(_database.EnviromentDbSize));
            _historyDbSizeSensor.AddValue(GetRoundedDouble(_database.SensorHistoryDbSize));
            _layoutDbSizeSensor.AddValue(GetRoundedDouble(_database.ServerLayoutDbSize));
            _snapshotsDbSizeSensor.AddValue(GetRoundedDouble(_database.Snapshots.Size));
            _journalDbSizeSensor.AddValue(GetRoundedDouble(_database.JournalDbSize));
            _backupsSizeSensor.AddValue(GetRoundedDouble(_database.BackupsSize));
            _dbSizeSensor.AddValue(GetRoundedDouble(_database.TotalDbSize));
        }

        private IInstantValueSensor<double> CreateDataSizeSensor(string databaseName)
        {
            var options = new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = true,
                SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.MB,

                Description = $"The sensor sends information about {databaseName} database size. {Descriptions[databaseName]}"
            };

            return _collector.CreateDoubleSensor($"Database/{databaseName} data size", options);
        }
    }
}
