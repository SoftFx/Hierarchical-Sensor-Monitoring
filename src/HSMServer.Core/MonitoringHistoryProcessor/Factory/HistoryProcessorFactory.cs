using HSMSensorDataObjects;
using HSMServer.Core.MonitoringHistoryProcessor.Processor;
using System;

namespace HSMServer.Core.MonitoringHistoryProcessor.Factory
{
    public class HistoryProcessorFactory : IHistoryProcessorFactory
    {
        public HistoryProcessorFactory(){}

        public IHistoryProcessor CreateProcessor(SensorType sensorType, PeriodType periodType)
        {
            TimeSpan convertedPeriod = ConvertPeriod(periodType);
            switch (sensorType)
            {
                case SensorType.DoubleBarSensor:
                    return new DoubleBarHistoryProcessor(convertedPeriod);
                case SensorType.IntegerBarSensor:
                    return new IntBarHistoryProcessor(convertedPeriod);
                case SensorType.DoubleSensor:
                    return new DoubleHistoryProcessor(convertedPeriod);
                case SensorType.IntSensor:
                    return new IntHistoryProcessor(convertedPeriod);
                case SensorType.BooleanSensor:
                    return new BoolHistoryProcessor(convertedPeriod);
                case SensorType.StringSensor:
                    return new StringHistoryProcessor(convertedPeriod);
                //Types that typically won't occur in that case
                default:
                    return new EmptyHistoryProcessor(convertedPeriod);
            }
        }

        public IHistoryProcessor CreateProcessor(SensorType sensorType)
        {
            switch (sensorType)
            {
                case SensorType.DoubleBarSensor:
                    return new DoubleBarHistoryProcessor();
                case SensorType.IntegerBarSensor:
                    return new IntBarHistoryProcessor();
                case SensorType.DoubleSensor:
                    return new DoubleHistoryProcessor();
                case SensorType.IntSensor:
                    return new IntHistoryProcessor();
                case SensorType.BooleanSensor:
                    return new BoolHistoryProcessor();
                case SensorType.StringSensor:
                    return new StringHistoryProcessor();
                //Types that typically won't occur in that case
                default:
                    return new EmptyHistoryProcessor();
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