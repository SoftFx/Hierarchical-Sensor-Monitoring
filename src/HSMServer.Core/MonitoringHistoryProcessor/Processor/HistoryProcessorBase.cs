using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal abstract class HistoryProcessorBase : IHistoryProcessor
    {
        protected TimeSpan PeriodInterval;

        protected HistoryProcessorBase(TimeSpan periodInterval)
        {
            PeriodInterval = periodInterval;
        }

        public virtual List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        {
            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            return uncompressedData;
        }
        public abstract string GetCsvHistory(List<SensorHistoryData> originalData);
    }
}
