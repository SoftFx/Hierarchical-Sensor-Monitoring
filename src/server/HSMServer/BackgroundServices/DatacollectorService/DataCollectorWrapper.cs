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
        private const int DigitsCnt = 2;

        private const string SelfMonitoringProductName = "HSM Server Monitoring";

        private const string RequestsCountPath = "Load/Requests per second";
        private const string DataCountPath = "Load/Received data count per second";

        private const string ResponseSizePath = "Load/Sent data per second KB";
        private const string RequestSizePath = "Load/Received data per second KB";

        private readonly IDataCollector _collector;

        private readonly TimeSpan _barInterval = new(0, 1, 0);


        internal DatabaseSize DbSizeSensors { get; }


        internal IParamsFuncSensor<double, double> RequestSizeSensor { get; }

        internal IParamsFuncSensor<double, double> ResponseSizeSensor { get; }

        internal IParamsFuncSensor<double, double> ReceivedDataCountSensor { get; }

        internal IParamsFuncSensor<double, double> RequestsCountSensor { get; }


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

            RequestSizeSensor = RegisterParamSensor<double>(RequestSizePath);
            ResponseSizeSensor = RegisterParamSensor<double>(ResponseSizePath);

            ReceivedDataCountSensor = RegisterParamSensor<double>(DataCountPath);
            RequestsCountSensor = RegisterParamSensor<double>(RequestsCountPath);

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