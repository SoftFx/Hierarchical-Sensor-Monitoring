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

        private const string EnvironmentDbSizePath = "Environment";
        private const string HistoryDbSizePath = "SensorValues";
        private const string LayoutDbSizePath = "ServerLayout";
        private const string SnaphotsDbSizePath = "Snapshots";
        private const string JournalsDbSizePath = "Journals";
        private const string TotalDbSizePath = "All";


        private readonly IDataCollector _collector;
        private readonly IDatabaseCore _database;

        private readonly IInstantValueSensor<double> _environmentDbSizeSensor;
        private readonly IInstantValueSensor<double> _snapshotsDbSizeSensor;
        private readonly IInstantValueSensor<double> _historyDbSizeSensor;
        private readonly IInstantValueSensor<double> _journalDbSizeSensor;
        private readonly IInstantValueSensor<double> _layoutDbSizeSensor;
        private readonly IInstantValueSensor<double> _dbSizeSensor;


        private readonly Dictionary<string, string> Descriptions = new()
        {
            { EnvironmentDbSizePath, "The database contains all server entities meta information (folders, products, sensors, users and etc.)." },
            { HistoryDbSizePath, "The database contains sensors history divided into weekly folders." },
            { LayoutDbSizePath, "The database contains information about dashboards (meta information, information about charts, panels and etc.)." },
            { SnaphotsDbSizePath, "The database contains current state of tree (last update time of sensors, timeouts and etc.)." },
            { JournalsDbSizePath, "The database contains journal records for each sensor." },
            { TotalDbSizePath, "All database size is the sum of the sizes of Environment, SensorValues, Snapshots, ServerLayout and Journals databases." },
        };


        internal DatabaseSize(IDataCollector collector, IDatabaseCore database)
        {
            _collector = collector;
            _database = database;

            _environmentDbSizeSensor = CreateDataSizeSensor(EnvironmentDbSizePath);
            _snapshotsDbSizeSensor = CreateDataSizeSensor(SnaphotsDbSizePath);
            _historyDbSizeSensor = CreateDataSizeSensor(HistoryDbSizePath);
            _journalDbSizeSensor = CreateDataSizeSensor(JournalsDbSizePath);
            _layoutDbSizeSensor = CreateDataSizeSensor(LayoutDbSizePath);
            _dbSizeSensor = CreateDataSizeSensor(TotalDbSizePath);
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
