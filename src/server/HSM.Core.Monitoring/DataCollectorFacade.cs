using HSMCommon.Constants;
using HSMCommon.Extensions;
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;
using HSMServer.Core.Cache;
using System;
using System.Linq;
using System.Reflection;

namespace HSM.Core.Monitoring
{
    public class DataCollectorFacade : IDataCollectorFacade, IDisposable
    {
        private const double KbDivisor = 1 << 10;
        private const double MbDivisor = 1 << 20;

        private const string SelfMonitoringProductName = "HSM Server Monitoring";

        private const string RequestSizeSensorPath = "Load/Received data per second KB";
        private const string SensorsCountSensorPath = "Load/Received sensors per second";
        private const string RequestsCountSensorPath = "Load/Requests per second";
        private const string ResponseSizeSensorPath = "Load/Sent data per second KB";

        private const string DatabaseSizePath = "Database/All database size MB";
        private const string EnvironmentDataSizePath = "Database/Environment data size MB";
        private const string SensorsHistoryDataSizePath = "Database/Monitoring data size MB";

        private readonly IDataCollector _dataCollector;

        private IParamsFuncSensor<double, double> _requestSizeSensor;
        private IParamsFuncSensor<double, double> _responseSizeSensor;
        private IParamsFuncSensor<double, int> _receivedSensorsSensor;
        private IParamsFuncSensor<double, int> _requestsCountSensor;
        private IInstantValueSensor<double> _databaseSizeSensor;
        private IInstantValueSensor<double> _sensorsHistoryDataSizeSensor;
        private IInstantValueSensor<double> _environmentDataSizeSensor;


        public DataCollectorFacade(ITreeValuesCache cache)
        {
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


            _dataCollector = new DataCollector(collectorOptions).AddNLog();

            if (OperatingSystem.IsWindows())
            {
                _dataCollector.Windows.AddProcessMonitoringSensors()
                                      .AddDiskMonitoringSensors()
                                      .AddSystemMonitoringSensors()
                                      .AddWindowsInfoMonitoringSensors()
                                      .AddProductVersion(productInfoOptions)
                                      .AddCollectorMonitoringSensors(collectorInfoOptions);
            }
            else
            {
                _dataCollector.Unix.AddProcessMonitoringSensors()
                                   .AddDiskMonitoringSensors()
                                   .AddSystemMonitoringSensors()
                                   .AddProductVersion(productInfoOptions)
                                   .AddCollectorMonitoringSensors(collectorInfoOptions);
            }

            InitializeSensors();

            _dataCollector.Start();
        }


        private static string GetSelfMonitoringKey(ITreeValuesCache cache)
        {
            var selfMonitoring = cache.GetProductByName(SelfMonitoringProductName);
            selfMonitoring ??= cache.AddProduct(SelfMonitoringProductName);

            var key = selfMonitoring.AccessKeys.FirstOrDefault(k => k.Value.DisplayName == CommonConstants.DefaultAccessKey).Key;

            return key.ToString();
        }

        #region Sensors creation

        private void InitializeSensors()
        {
            #region Load sensors

            //Request size sensor
            _requestSizeSensor = _dataCollector.CreateParamsFuncSensor<double, double>(
                RequestSizeSensorPath, "", valuesList => Math.Round(valuesList.Sum() / 45.0, 2, MidpointRounding.AwayFromZero), 45000);

            //Response size sensor
            _responseSizeSensor = _dataCollector.CreateParamsFuncSensor<double, double>(
                ResponseSizeSensorPath, "", valuesList => Math.Round(valuesList.Sum() / 45.0, 2, MidpointRounding.AwayFromZero), 45000);

            //Received sensors count
            _receivedSensorsSensor = _dataCollector.CreateParamsFuncSensor<double, int>(
                SensorsCountSensorPath, "", valuesList => Math.Round(valuesList.Sum() / 45.0, 2, MidpointRounding.AwayFromZero), 45000);

            //Requests count sensor
            _requestsCountSensor = _dataCollector.CreateParamsFuncSensor<double, int>(
                RequestsCountSensorPath, "", valuesList => Math.Round(valuesList.Sum() / 45.0, 2, MidpointRounding.AwayFromZero), 45000);

            #endregion

            #region Database sensors

            //Database size sensor
            _databaseSizeSensor = _dataCollector.CreateDoubleSensor(DatabaseSizePath);

            //Monitoring data size sensor
            _sensorsHistoryDataSizeSensor = _dataCollector.CreateDoubleSensor(SensorsHistoryDataSizePath);

            //Environment data size sensor
            _environmentDataSizeSensor = _dataCollector.CreateDoubleSensor(EnvironmentDataSizePath);

            #endregion
        }

        #endregion
        #region Database size reporting

        public void ReportDatabaseSize(long bytesSize)
        {
            double mbSize = bytesSize / MbDivisor;
            _databaseSizeSensor.AddValue(Math.Round(mbSize, 2, MidpointRounding.AwayFromZero));
        }

        public void ReportSensorsHistoryDataSize(long bytesSize)
        {
            double mbSize = bytesSize / MbDivisor;
            _sensorsHistoryDataSizeSensor.AddValue(Math.Round(mbSize, 2, MidpointRounding.AwayFromZero));
        }

        public void ReportEnvironmentDataSize(long bytesSize)
        {
            double mbSize = bytesSize / MbDivisor;
            _environmentDataSizeSensor.AddValue(Math.Round(mbSize, 2, MidpointRounding.AwayFromZero));
        }

        #endregion

        #region Load reporting

        public void ReportRequestSize(double size)
        {
            double processedSize = size / KbDivisor;
            _requestSizeSensor.AddValue(processedSize);
        }

        public void ReportSensorsCount(int count)
        {
            _receivedSensorsSensor.AddValue(count);
        }

        public void ReportResponseSize(double size)
        {
            double processedSize = size / KbDivisor;
            _responseSizeSensor.AddValue(processedSize);
        }

        public void IncreaseRequestsCount(int count = 1)
        {
            _requestsCountSensor.AddValue(count);
        }

        #endregion


        public void Dispose()
        {
            _dataCollector?.Dispose();
        }
    }
}
