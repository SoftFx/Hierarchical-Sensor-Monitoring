using System;
using System.Collections.Generic;
using HSMCommon.Model.SensorsData;

namespace HSMServer.MonitoringHistoryProcessor.Processor
{
    internal abstract class HistoryProcessorBase : IHistoryProcessor
    {
        protected TimeSpan PeriodInterval;

        protected HistoryProcessorBase(TimeSpan periodInterval)
        {
            PeriodInterval = periodInterval;
        }

        public abstract List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData);
    }
}
