using HSMSensorDataObjects;
using System;
using HSMServer.MonitoringHistoryProcessor.Processor;

namespace HSMServer.MonitoringHistoryProcessor.Factory
{
    internal class HistoryProcessorFactory : IHistoryProcessorFactory
    {
        public HistoryProcessorFactory(){}

        public IHistoryProcessor CreateProcessor(SensorType sensorType, PeriodType periodType)
        {
            TimeSpan convertedPeriod = ConvertPeriod(periodType);
            switch (sensorType)
            {
                case SensorType.DoubleSensor:
                    return new DoubleHistoryProcessor(convertedPeriod);
                case SensorType.DoubleBarSensor:
                    return new DoubleBarHistoryProcessor(convertedPeriod);
                case SensorType.IntegerBarSensor:
                    return new IntBarHistoryProcessor(convertedPeriod);
                case SensorType.IntSensor:
                    return new IntHistoryProcessor(convertedPeriod);
                //Do nothing for strings and booleans
                case SensorType.BooleanSensor:
                case SensorType.StringSensor:
                    return new EmptyHistoryProcessor(convertedPeriod);
                //Types that typically won't occur in that case
                default:
                    return new EmptyHistoryProcessor(convertedPeriod);
            }
        }


        private TimeSpan ConvertPeriod(PeriodType periodType)
        {
            switch (periodType)
            {
                case PeriodType.Hour:
                    return TimeSpan.FromMinutes(5);
                case PeriodType.Day:
                    return TimeSpan.FromHours(1);
                case PeriodType.ThreeDays:
                    return TimeSpan.FromHours(3);
                case PeriodType.Week:
                    return TimeSpan.FromHours(6);
                case PeriodType.Month:
                    return TimeSpan.FromDays(1);
            }
            return TimeSpan.FromDays(1);
        }
    }
}