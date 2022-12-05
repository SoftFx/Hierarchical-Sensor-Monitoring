using HSMServer.Core.Model;
using HSMServer.Core.MonitoringHistoryProcessor.Processor;

namespace HSMServer.Core.MonitoringHistoryProcessor.Factory
{
    public static class HistoryProcessorFactory
    {
        public static IHistoryProcessor BuildProcessor(int sensorType = -1) =>
            (SensorType)sensorType switch
            {
                SensorType.Boolean => new BoolHistoryProcessor(),
                SensorType.Integer => new IntHistoryProcessor(),
                SensorType.Double => new DoubleHistoryProcessor(),
                SensorType.String => new StringHistoryProcessor(),
                SensorType.IntegerBar => new IntBarHistoryProcessor(),
                SensorType.DoubleBar => new DoubleBarHistoryProcessor(),
                _ => new EmptyHistoryProcessor(), // Types that typically won't occur in that case
            };
    }
}