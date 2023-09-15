using HSMServer.Core.Model;
using HSMServer.Model.TreeViewModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Model.History
{
    internal abstract class HistoryProcessorBase
    {
        public List<BaseValue> ProcessingAndCompression(SensorNodeViewModel sensor, List<BaseValue> values, int compressedValuesCount)
        {
            DateTime ByReceivingTime(BaseValue value) => value.ReceivingTime;

            DateTime ByTime(BaseValue value) => value.Time;


            Func<BaseValue, DateTime> orderByFilter = sensor.AggregateValues ? ByReceivingTime : ByTime;
            Func<BaseValue, DateTime> thenByFilter = sensor.AggregateValues ? ByTime : ByReceivingTime;

            values = values.OrderBy(orderByFilter)
                           .ThenBy(thenByFilter)
                           .Select(v => v with { Time = v.Time.ToUniversalTime() })
                           .ToList();

            if (values.Count < compressedValuesCount)
                return values;

            var interval = CountInterval(values, compressedValuesCount);
            if (interval == TimeSpan.Zero)
                return values;

            return Compress(values, interval);
        }

        protected virtual List<BaseValue> Compress(List<BaseValue> history, TimeSpan compressionInterval) => history;

        private static TimeSpan CountInterval(List<BaseValue> values, int compressedValuesCount)
        {
            var fullTime = values.Last().Time - values.First().Time;
            var fullMilliseconds = fullTime.TotalMilliseconds;
            var intervalMilliseconds = fullMilliseconds / compressedValuesCount;

            return TimeSpan.FromMilliseconds(intervalMilliseconds);
        }
    }
}
