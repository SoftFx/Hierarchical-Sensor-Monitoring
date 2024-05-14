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
        private const string SelfCollectorName = "Self monitoring";
        public const string SelfMonitoringProductName = "HSM Server Monitoring";

        private readonly IDataCollector _collector;


        internal DatabaseSensorsStatistics DbStatisticsSensors { get; }

        internal ClientStatisticsSensors WebRequestsSensors { get; }

        internal DatabaseSensorsSize DbSizeSensors { get; }


        public DataCollectorWrapper(ITreeValuesCache cache, IDatabaseCore db, IServerConfig config)
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

            DbStatisticsSensors = new DatabaseSensorsStatistics(_collector, db, cache, config);
            DbSizeSensors = new DatabaseSensorsSize(_collector, db, config);
            WebRequestsSensors = new ClientStatisticsSensors(_collector);
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