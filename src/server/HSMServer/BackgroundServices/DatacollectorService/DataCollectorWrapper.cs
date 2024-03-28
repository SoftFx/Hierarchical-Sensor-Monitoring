using HSMCommon.Constants;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Extensions;
using HSMServer.ServerConfiguration;
using Microsoft.Extensions.Options;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class DataCollectorWrapper : IDisposable
    {
        private const string SelfMonitoringProductName = "HSM Server Monitoring";
        private const string SelfCollectorName = "Self monitoring";

        private readonly IDataCollector _collector;


        internal DatabaseSensorsStatistics DbStatisticsSensors { get; }

        internal DatabaseSensorsSize DbSizeSensors { get; }

        internal ClientStatistics Statistics { get; }


        public DataCollectorWrapper(ITreeValuesCache cache, IDatabaseCore db, IServerConfig config, IOptionsMonitor<MonitoringOptions> optionsMonitor)
        {
            var productVersion = Assembly.GetEntryAssembly()?.GetName().GetVersion();
            var loggerOptions = new LoggerOptions()
            {
                WriteDebug = false,
            };

            var options = new CollectorOptions
            {
                AccessKey = GetSelfMonitoringKey(cache),
                ClientName = SelfCollectorName,
            };

            _collector = new DataCollector(options).AddNLog(loggerOptions);

            if (OperatingSystem.IsWindows())
                _collector.Windows.AddAllDefaultSensors(productVersion);
            else
                _collector.Unix.AddAllDefaultSensors(productVersion);

            Statistics = new ClientStatistics(_collector, optionsMonitor);
            DbSizeSensors = new DatabaseSensorsSize(_collector, db, config);
            DbStatisticsSensors = new DatabaseSensorsStatistics(_collector, db, cache, config);
        }


        public void Dispose() => _collector?.Dispose();

        internal Task Start() => _collector.Start();

        internal Task Stop() => _collector.Stop();


        internal void SendDbInfo()
        {
            DbSizeSensors.SendInfo();
            DbStatisticsSensors.SendInfo();
        }


        private static string GetSelfMonitoringKey(ITreeValuesCache cache)
        {
            var selfMonitoring = cache.GetProductByName(SelfMonitoringProductName);
            selfMonitoring ??= cache.AddProduct(SelfMonitoringProductName, Guid.Empty);

            var key = selfMonitoring.AccessKeys.FirstOrDefault(k => k.Value.DisplayName == CommonConstants.DefaultAccessKey).Key;

            return key.ToString();
        }
    }
}