using HSMServer.Core.Model.Sensor;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal abstract class HistoryProcessorBase : IHistoryProcessor
    {
        protected TimeSpan PeriodInterval;
        protected const int ExpectedBarCount = 30;

        protected HistoryProcessorBase(TimeSpan periodInterval)
        {
            PeriodInterval = periodInterval;
        }

        protected HistoryProcessorBase()
        {

        }


        private TimeSpan CountInterval(List<SensorHistoryData> unprocessedData)
        {
            var fullTime = unprocessedData.Last().Time - unprocessedData.First().Time;
            var fullMilliseconds = fullTime.TotalMilliseconds;
            var intervalMilliseconds = fullMilliseconds / ExpectedBarCount;
            return TimeSpan.FromMilliseconds(intervalMilliseconds);
        }



        protected virtual List<SensorHistoryData> ProcessHistoryInternal(
            List<SensorHistoryData> unprocessedHistory, TimeSpan compressionInterval)
        {
            return unprocessedHistory;
        }

        //public virtual List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        //{
        //    uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
        //    return uncompressedData;
        //}

        public List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        {
            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            if (uncompressedData.Count < 2)
                return uncompressedData;

            var interval = CountInterval(uncompressedData);
            return ProcessHistoryInternal(uncompressedData, interval);
        }

        public abstract string GetCsvHistory(List<SensorHistoryData> originalData);
    }
}
