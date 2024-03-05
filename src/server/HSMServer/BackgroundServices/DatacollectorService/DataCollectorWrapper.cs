using HSMCommon.Constants;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.PublicInterface;
using HSMServer.Core.Cache;
using HSMServer.Core.DataLayer;
using HSMServer.Extensions;
using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace HSMServer.BackgroundServices
{
    public sealed class DataCollectorWrapper : IDisposable
    {
        private const string SelfMonitoringProductName = "HSM Server Monitoring";

        private const string RequestsCountPath = "Load/Requests per second";
        private const string DataCountPath = "Load/Received data count per second";

        private const string ResponseSizePath = "Load/Sent data per second KB";
        private const string RequestSizePath = "Load/Received data per second KB";

        private readonly IDataCollector _collector;


        internal DatabaseSize DbSizeSensors { get; }


        internal IMonitoringRateSensor ResponseSizeSensor { get; }

        internal IMonitoringRateSensor RequestSizeSensor { get; }


        internal IMonitoringRateSensor ReceivedDataCountSensor { get; }

        internal IMonitoringRateSensor RequestsCountSensor { get; }


        public DataCollectorWrapper(IDatabaseCore database, ITreeValuesCache cache)
        {
            var productVersion = Assembly.GetEntryAssembly()?.GetName().GetVersion();
            var loggerOptions = new LoggerOptions()
            {
                WriteDebug = false,
            };

            _collector = new DataCollector(GetSelfMonitoringKey(cache)).AddNLog(loggerOptions);

            if (OperatingSystem.IsWindows())
                _collector.Windows.AddAllDefaultSensors(productVersion);
            else
                _collector.Unix.AddAllDefaultSensors(productVersion);

            ResponseSizeSensor = _collector.CreateM1RateSensor(ResponseSizePath);
            RequestSizeSensor = _collector.CreateM1RateSensor(RequestSizePath);

            ReceivedDataCountSensor = _collector.CreateM1RateSensor(DataCountPath);
            RequestsCountSensor = _collector.CreateM1RateSensor(RequestsCountPath);

            DbSizeSensors = new DatabaseSize(_collector, database);
        }


        public void Dispose() => _collector?.Dispose();

        internal Task Start() => _collector.Start();

        internal Task Stop() => _collector.Stop();


        internal void SendDbInfo() => DbSizeSensors.SendInfo();


        private static string GetSelfMonitoringKey(ITreeValuesCache cache)
        {
            var selfMonitoring = cache.GetProductByName(SelfMonitoringProductName);
            selfMonitoring ??= cache.AddProduct(SelfMonitoringProductName, Guid.Empty);

            var key = selfMonitoring.AccessKeys.FirstOrDefault(k => k.Value.DisplayName == CommonConstants.DefaultAccessKey).Key;

            return key.ToString();
        }
    }
}