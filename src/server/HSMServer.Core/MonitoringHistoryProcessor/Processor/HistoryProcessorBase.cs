using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal abstract class HistoryProcessorBase : IHistoryProcessor
    {
        private const int ExpectedBarCount = 30;


        public abstract string GetCsvHistory(List<BaseValue> originalData);

        public List<BaseValue> ProcessHistory(List<BaseValue> values)
        {
            values = values.OrderBy(v => v.Time).ThenBy(v => v.ReceivingTime).ToList();

            if (values.Count < 2)
                return values;

            var interval = CountInterval(values);
            return ProcessHistory(values, interval);
        }

        protected virtual List<BaseValue> ProcessHistory(List<BaseValue> unprocessedHistory, TimeSpan compressionInterval) =>
            unprocessedHistory;

        private static TimeSpan CountInterval(List<BaseValue> values)
        {
            var fullTime = values.Last().Time - values.First().Time;
            var fullMilliseconds = fullTime.TotalMilliseconds;
            var intervalMilliseconds = fullMilliseconds / ExpectedBarCount;

            return TimeSpan.FromMilliseconds(intervalMilliseconds);
        }
    }
}
