using HSMCommon.Model;


namespace HSMServer.Model.History
{
    public static class HistoryProcessorFactory
    {
        internal static HistoryProcessorBase BuildProcessor(int sensorType = -1) =>
            (SensorType)sensorType switch
            {
                SensorType.Boolean => new BoolHistoryProcessor(),
                SensorType.Integer => new IntHistoryProcessor(),
                SensorType.Double => new DoubleHistoryProcessor(),
                SensorType.Rate => new DoubleHistoryProcessor(),
                SensorType.String => new StringHistoryProcessor(),
                SensorType.IntegerBar => new IntBarHistoryProcessor(),
                SensorType.DoubleBar => new DoubleBarHistoryProcessor(),
                SensorType.TimeSpan => new TimeSpanHistoryProcessor(),
                SensorType.Version => new VersionHistoryProcessor(),
                SensorType.Enum => new EnumHistoryProcessor(),
                _ => new EmptyHistoryProcessor(), // Types that typically won't occur in that case
            };
    }
}