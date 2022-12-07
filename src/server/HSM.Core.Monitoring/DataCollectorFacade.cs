using HSMCommon.Constants;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMServer.Core.Cache;
using System;
using System.Linq;

namespace HSM.Core.Monitoring
{
    public class DataCollectorFacade : IDataCollectorFacade, IDisposable
    {
        private const double KbDivisor = 1 << 10;
        private const double MbDivisor = 1 << 20;
        private const string SelfMonitoringProductName = "HSM Server Monitoring";

        private readonly IDataCollector _dataCollector;

        private IParamsFuncSensor<double, double> _requestSizeSensor;
        private IParamsFuncSensor<double, double> _responseSizeSensor;
        private IParamsFuncSensor<double, int> _receivedSensorsSensor;
        private IParamsFuncSensor<double, int> _requestsCountSensor;
        private IInstantValueSensor<double> _databaseSizeSensor;
        private IInstantValueSensor<double> _monitoringDataSizeSensor;
        private IInstantValueSensor<double> _environmentDataSizeSensor;


        public DataCollectorFacade(ITreeValuesCache cache)
        {
            _dataCollector = new DataCollector(GetSelfMonitoringKey(cache), "https://localhost");
            _dataCollector.Initialize(true);
            _dataCollector.InitializeProcessMonitoring(true, true, true);

            InitializeSensors();
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
            _requestSizeSensor = _dataCollector.CreateParamsFuncSensor<double, double>
            (MonitoringConstants.RequestSizeSensorPath, "",
                valuesList => Math.Round(valuesList.Sum() / 45.0, 2, MidpointRounding.AwayFromZero), 45000);

            //Response size sensor
            _responseSizeSensor = _dataCollector.CreateParamsFuncSensor<double, double>
            (MonitoringConstants.ResponseSizeSensorPath, "",
                valuesList => Math.Round(valuesList.Sum() / 45.0, 2, MidpointRounding.AwayFromZero),
                45000);

            //Received sensors count
            _receivedSensorsSensor = _dataCollector.CreateParamsFuncSensor<double, int>(
                MonitoringConstants.SensorsCountSensorPath, "",
                valuesList => Math.Round(valuesList.Sum() / 45.0, 2, MidpointRounding.AwayFromZero),
                45000);

            //Requests count sensor
            _requestsCountSensor = _dataCollector.CreateParamsFuncSensor<double, int>(
                MonitoringConstants.RequestsCountSensorPath, "",
                valuesList => Math.Round(valuesList.Sum() / 45.0, 2, MidpointRounding.AwayFromZero),
                45000);

            #endregion

            #region Database sensors

            //Database size sensor
            _databaseSizeSensor = _dataCollector.CreateDoubleSensor(MonitoringConstants.DatabaseSizePath);

            //Monitoring data size sensor
            _monitoringDataSizeSensor = _dataCollector
                .CreateDoubleSensor(MonitoringConstants.MonitoringDataSizePath);

            //Environment data size sensor
            _environmentDataSizeSensor = _dataCollector
                .CreateDoubleSensor(MonitoringConstants.EnvironmentDataSizePath);

            #endregion
        }

        #endregion
        #region Database size reporting

        public void ReportDatabaseSize(long bytesSize)
        {
            double mbSize = bytesSize / MbDivisor;
            _databaseSizeSensor.AddValue(Math.Round(mbSize, 2, MidpointRounding.AwayFromZero));
        }

        public void ReportMonitoringDataSize(long bytesSize)
        {
            double mbSize = bytesSize / MbDivisor;
            _monitoringDataSizeSensor.AddValue(Math.Round(mbSize, 2, MidpointRounding.AwayFromZero));
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
