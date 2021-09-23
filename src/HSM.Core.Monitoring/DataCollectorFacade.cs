using HSMCommon.Constants;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using System;
using System.Linq;

namespace HSM.Core.Monitoring
{
    public class DataCollectorFacade : IDataCollectorFacade, IDisposable
    {
        private readonly IDataCollector _dataCollector;
        private IParamsFuncSensor<double, double> _requestSizeSensor;
        private IParamsFuncSensor<double, double> _responseSizeSensor;
        private IParamsFuncSensor<double, int> _receivedSensorsSensor;
        private IParamsFuncSensor<double, int> _requestsCountSensor;
        private IInstantValueSensor<double> _databaseSizeSensor;
        private IInstantValueSensor<double> _monitoringDataSizeSensor;
        private IInstantValueSensor<double> _environmentDataSizeSensor;
        private const double _kbDivisor = 1024.0;
        private const double _mbDivisor = 1048576.0;
        public DataCollectorFacade()
        {
            _dataCollector = new DataCollector(CommonConstants.SelfMonitoringProductKey,
                "https://localhost");
            _dataCollector.Initialize(true);
            _dataCollector.InitializeProcessMonitoring(true, true, true);
            InitializeSensors();
        }

        #region Sensors creation

        private void InitializeSensors()
        {
            #region Load sensors

            //Request size sensor
            _requestSizeSensor = _dataCollector.CreateParamsFuncSensor<double, double>
            (MonitoringConstants.RequestSizeSensorPath, "",
                valuesList => Math.Round(valuesList.Sum() / 15.0, 2, MidpointRounding.AwayFromZero));

            //Response size sensor
            _responseSizeSensor = _dataCollector.CreateParamsFuncSensor<double, double>
            (MonitoringConstants.ResponseSizeSensorPath, "",
                valuesList => Math.Round(valuesList.Sum() / 15.0, 2, MidpointRounding.AwayFromZero));

            //Received sensors count
            _receivedSensorsSensor = _dataCollector.CreateParamsFuncSensor<double, int>(
                MonitoringConstants.SensorsCountSensorPath, "",
                valuesList => Math.Round(valuesList.Sum() / 15.0, 2, MidpointRounding.AwayFromZero));

            //Requests count sensor
            _requestsCountSensor = _dataCollector.CreateParamsFuncSensor<double, int>(
                MonitoringConstants.RequestsCountSensorPath, "",
                valuesList => Math.Round(valuesList.Sum() / 15.0, 2, MidpointRounding.AwayFromZero));

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
            double mbSize = bytesSize / _mbDivisor;
            _databaseSizeSensor.AddValue(Math.Round(mbSize, 2, MidpointRounding.AwayFromZero));
        }

        public void ReportMonitoringDataSize(long bytesSize)
        {
            double mbSize = bytesSize / _mbDivisor;
            _monitoringDataSizeSensor.AddValue(Math.Round(mbSize, 2, MidpointRounding.AwayFromZero));
        }

        public void ReportEnvironmentDataSize(long bytesSize)
        {
            double mbSize = bytesSize / _mbDivisor;
            _environmentDataSizeSensor.AddValue(Math.Round(mbSize, 2, MidpointRounding.AwayFromZero));
        }

        #endregion

        #region Load reporting

        public void ReportRequestSize(double size)
        {
            double processedSize = size / _kbDivisor;
            _requestSizeSensor.AddValue(processedSize);
        }

        public void ReportSensorsCount(int count)
        {
            _receivedSensorsSensor.AddValue(count);
        }

        public void ReportResponseSize(double size)
        {
            double processedSize = size / _kbDivisor;
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
