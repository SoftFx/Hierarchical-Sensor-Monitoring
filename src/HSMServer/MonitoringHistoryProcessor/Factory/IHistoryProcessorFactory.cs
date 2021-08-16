using HSMSensorDataObjects;
using HSMServer.MonitoringHistoryProcessor.Processor;

namespace HSMServer.MonitoringHistoryProcessor.Factory
{
    public interface IHistoryProcessorFactory
    {
        IHistoryProcessor CreateProcessor(SensorType sensorType, PeriodType periodType);
    }
}