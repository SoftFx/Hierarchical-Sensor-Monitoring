using HSMCommon.Constants;
using HSMCommon.Extensions;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class DataCollectorWrapper : IDisposable
    {
        private const int DigitsCnt = 2;

        private const double MbDivisor = 1 << 20;

        private const string SelfMonitoringProductName = "HSM Server Monitoring";

        private const string RequestsCountPath = "Load/Requests per second";
        private const string DataCountPath = "Load/Received data count per second";

        private const string ResponseSizePath = "Load/Sent data per second KB";
        private const string RequestSizePath = "Load/Received data per second KB";

        private const string EnvironmentDbSizePath = "Database/Environment data size MB";
        private const string SnaphotsDbSizePath = "Database/Snapshots data size MB";
        private const string HistoryDbSizePath = "Database/Monitoring data size MB";
        private const string TotalDbSizePath = "Database/All database size MB";

        private readonly IInstantValueSensor<double> _environmentDbSizeSensor;
        private readonly IInstantValueSensor<double> _snapshotsDbSizeSensor;
        private readonly IInstantValueSensor<double> _historyDbSizeSensor;
        private readonly IInstantValueSensor<double> _dbSizeSensor;
        private readonly IDataCollector _collector;
        private readonly IDatabaseCore _database;

        private readonly TimeSpan _barInterval = new(0, 1, 0);


        internal IParamsFuncSensor<double, double> RequestSizeSensor { get; }

        internal IParamsFuncSensor<double, double> ResponseSizeSensor { get; }

        internal IParamsFuncSensor<double, double> ReceivedDataCountSensor { get; }

        internal IParamsFuncSensor<double, double> RequestsCountSensor { get; }


        public DataCollectorWrapper(IDatabaseCore database, ITreeValuesCache cache)
        {
            _database = database;

            var collectorOptions = new CollectorOptions()
            {
                AccessKey = GetSelfMonitoringKey(cache),
                ServerAddress = "https://localhost",
            };

            var productInfoOptions = new VersionSensorOptions()
            {
                Version = Assembly.GetEntryAssembly()?.GetName().GetVersion()
            };

            var collectorInfoOptions = new CollectorMonitoringInfoOptions();


            _collector = new DataCollector(collectorOptions).AddNLog();

            if (OperatingSystem.IsWindows())
            {
                _collector.Windows.AddProcessMonitoringSensors()
                                  .AddDiskMonitoringSensors()
                                  .AddSystemMonitoringSensors()
                                  .AddWindowsInfoMonitoringSensors()
                                  .AddProductVersion(productInfoOptions)
                                  .AddCollectorMonitoringSensors(collectorInfoOptions);
            }
            else
            {
                _collector.Unix.AddProcessMonitoringSensors()
                               .AddDiskMonitoringSensors()
                               .AddSystemMonitoringSensors()
                               .AddProductVersion(productInfoOptions)
                               .AddCollectorMonitoringSensors(collectorInfoOptions);
            }

            RequestSizeSensor = RegisterParamSensor<double>(RequestSizePath);
            ResponseSizeSensor = RegisterParamSensor<double>(ResponseSizePath);

            ReceivedDataCountSensor = RegisterParamSensor<double>(DataCountPath);
            RequestsCountSensor = RegisterParamSensor<double>(RequestsCountPath);

            _dbSizeSensor = _collector.CreateDoubleSensor(TotalDbSizePath);
            _environmentDbSizeSensor = _collector.CreateDoubleSensor(EnvironmentDbSizePath);
            _snapshotsDbSizeSensor = _collector.CreateDoubleSensor(SnaphotsDbSizePath);
            _historyDbSizeSensor = _collector.CreateDoubleSensor(HistoryDbSizePath);
        }


        public void Dispose() => _collector?.Dispose();

        internal Task Start() => _collector.Start();

        internal Task Stop() => _collector.Stop();


        internal void SendDbInfo()
        {
            static double GetRoundedDouble(long sizeInBytes)
            {
                return Math.Round(sizeInBytes / MbDivisor, DigitsCnt, MidpointRounding.AwayFromZero);
            }

            _dbSizeSensor.AddValue(GetRoundedDouble(_database.TotalDbSize));
            _environmentDbSizeSensor.AddValue(GetRoundedDouble(_database.EnviromentDbSize));
            _historyDbSizeSensor.AddValue(GetRoundedDouble(_database.SensorHistoryDbSize));
            _snapshotsDbSizeSensor.AddValue(GetRoundedDouble(_database.Snapshots.Size));
        }


        private static string GetSelfMonitoringKey(ITreeValuesCache cache)
        {
            var selfMonitoring = cache.GetProductByName(SelfMonitoringProductName);
            selfMonitoring ??= cache.AddProduct(SelfMonitoringProductName, Guid.Empty);

            var key = selfMonitoring.AccessKeys.FirstOrDefault(k => k.Value.DisplayName == CommonConstants.DefaultAccessKey).Key;

            return key.ToString();
        }

        private IParamsFuncSensor<T, T> RegisterParamSensor<T>(string path) where T : IFloatingPoint<T>
        {
            static T GetSum(List<T> values)
            {
                return values.Aggregate(T.Zero, (sum, curVal) => sum + curVal);
            }

            var denominator = T.CreateChecked(_barInterval.TotalSeconds);

            return _collector.CreateParamsFuncSensor<T, T>(path, string.Empty, values => T.Round(GetSum(values) / denominator, DigitsCnt, MidpointRounding.AwayFromZero), _barInterval);
        }
    }
}