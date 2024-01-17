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

        private const string EnvironmentDbSizePath = "Environment data size";
        private const string HistoryDbSizePath = "SensorValues data size";
        private const string SnaphotsDbSizePath = "Snapshots data size";
        private const string TotalDbSizePath = "All database size";


        private readonly IDataCollector _collector;
        private readonly IDatabaseCore _database;

        private readonly IInstantValueSensor<double> _environmentDbSizeSensor;
        private readonly IInstantValueSensor<double> _snapshotsDbSizeSensor;
        private readonly IInstantValueSensor<double> _historyDbSizeSensor;
        private readonly IInstantValueSensor<double> _dbSizeSensor;


        private readonly Dictionary<string, string> Descriptions = new()
        {
            { EnvironmentDbSizePath, "The database contains all server entities meta information (folders, products, sensors, users, etc.)." },
            { SnaphotsDbSizePath, "The database contains current state of tree (last update time of sensors, timeouts, etc.)." },
            { HistoryDbSizePath, "The database contains sensors history divided into weekly folders." },
            { TotalDbSizePath, "All database size is the sum of the sizes of Environment, SensorValues, Snapshots, ServerLayouts and Journal databases." },
        };


        internal DatabaseSize(IDataCollector collector, IDatabaseCore database)
        {
            _collector = collector;
            _database = database;

            _environmentDbSizeSensor = CreateDataSizeSensor(EnvironmentDbSizePath);
            _snapshotsDbSizeSensor = CreateDataSizeSensor(SnaphotsDbSizePath);
            _historyDbSizeSensor = CreateDataSizeSensor(HistoryDbSizePath);
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
            _snapshotsDbSizeSensor.AddValue(GetRoundedDouble(_database.Snapshots.Size));
            _dbSizeSensor.AddValue(GetRoundedDouble(_database.TotalDbSize));
        }

        private IInstantValueSensor<double> CreateDataSizeSensor(string sensorName)
        {
            var databaseName = sensorName[..sensorName.IndexOf(' ')];
            var databaseDescription = Descriptions[sensorName];

            var options = new InstantSensorOptions
            {
                Alerts = [],
                TTL = TimeSpan.MaxValue,
                EnableForGrafana = true,
                SensorUnit = HSMSensorDataObjects.SensorRequests.Unit.MB,

                Description = $"The sensor sends information about {databaseName} database size. {databaseDescription}"
            };

            return _collector.CreateDoubleSensor($"Database/{sensorName}", options);
        }
    }
}
