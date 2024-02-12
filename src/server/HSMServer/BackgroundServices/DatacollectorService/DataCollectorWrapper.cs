using HSMCommon.Constants;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
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
        private const string SelfMonitoringProductName = "HSM Server Monitoring";

        private readonly IDataCollector _collector;
        

        internal DatabaseSize DbSizeSensors { get; }
        internal ClientStatistics Statistics { get; }


        public DataCollectorWrapper(IDataCollector collector, ClientStatistics statistics, DatabaseSize databaseSize)
        {
            var productVersion = Assembly.GetEntryAssembly()?.GetName().GetVersion();
            var loggerOptions = new LoggerOptions()
            {
                WriteDebug = false,
            };

            _collector = collector.AddNLog(loggerOptions);

            // if (OperatingSystem.IsWindows())
            //     _collector.Windows.AddAllDefaultSensors(productVersion);
            // else
            //     _collector.Unix.AddAllDefaultSensors(productVersion);

            Statistics = statistics;
            DbSizeSensors = databaseSize;
        }


        public void Dispose() => _collector?.Dispose();

        internal Task Start() => _collector.Start();

        internal Task Stop() => _collector.Stop();


        internal void SendDbInfo() => DbSizeSensors.SendInfo();


        public static string GetSelfMonitoringKey(ITreeValuesCache cache)
        {
            var selfMonitoring = cache.GetProductByName(SelfMonitoringProductName);
            selfMonitoring ??= cache.AddProduct(SelfMonitoringProductName, Guid.Empty);

            var key = selfMonitoring.AccessKeys.FirstOrDefault(k => k.Value.DisplayName == CommonConstants.DefaultAccessKey).Key;

            return key.ToString();
        }
    }
}