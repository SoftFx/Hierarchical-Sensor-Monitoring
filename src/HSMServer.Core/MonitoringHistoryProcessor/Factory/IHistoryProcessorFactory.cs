using HSMSensorDataObjects;
using HSMServer.Core.MonitoringHistoryProcessor.Processor;

namespace HSMServer.Core.MonitoringHistoryProcessor.Factory
{
    public interface IHistoryProcessorFactory
    {
        IHistoryProcessor CreateProcessor(SensorType sensorType, PeriodType periodType);
        IHistoryProcessor CreateProcessor(SensorType sensorType);
    }
}