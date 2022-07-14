using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal sealed class IntBarHistoryProcessor : HistoryProcessorBase
    {
        private readonly List<KeyValuePair<int, int>> _MeanList = new();


        protected override List<BaseValue> ProcessHistory(List<BaseValue> values, TimeSpan compressionInterval)
        {
            if (values == null || values.Count == 0)
                return new();

            if (values.Count == 1)
                return values;

            var result = new List<BaseValue>();

            var initTime = (values.First() as BarBaseValue).OpenTime;
            var summary = new SummaryBarItem<int>() { Count = 0, Max = int.MinValue, Min = int.MaxValue, OpenTime = initTime, CloseTime = initTime };
            var nextBarTime = initTime + compressionInterval;

            for (int i = 0; i < values.Count; ++i)
            {
                if (values[i] is not IntegerBarValue value || value.CloseTime == DateTime.MinValue)
                    continue;

                if (summary.CloseTime + (value.CloseTime - value.OpenTime) <= nextBarTime)
                {
                    ProcessItem(value, summary);
                }
                else
                {
                    result.Add(Convert(summary));
                    summary = new SummaryBarItem<int>() { Count = value.Count, Max = value.Max, Min = value.Min, OpenTime = value.OpenTime, CloseTime = value.CloseTime };
                    while (nextBarTime <= summary.CloseTime)
                        nextBarTime += compressionInterval;
                }
            }

            result.Add(Convert(summary));

            //var openTime = (values.First() as BarBaseValue).OpenTime;
            //int processingCount = 0;
            //bool needToAddCurrentAsSingle = false;
            //bool addingCurrent = false;

            //for (int i = values.Count - 1; i >= 0; --i)
            //{
            //    if (values[i] is not IntegerBarValue value)
            //        continue;

            //    //We must add current processed object if its period bigger than interval,
            //    //or if current object is longer than than interval, or if we are processing the last object
            //    if (value.CloseTime - value.OpenTime > compressionInterval ||
            //        (processingCount > 0 && openTime + compressionInterval < value.CloseTime && i == 0))
            //    {
            //        needToAddCurrentAsSingle = true;
            //    }

            //    //Just process current bar as usual if it is not the last & in the interval
            //    if (value.CloseTime < openTime + compressionInterval && i == 0)
            //    {
            //        AddValueToList(value);
            //        ProcessItem(value, summary);
            //        addingCurrent = true;
            //    }

            //    //Finish bar if necessary. We finish previous bar if we are adding current as single
            //    //or if we are processing the last bar or if next bar is not in the interval
            //    if (i < values.Count - 1 && (openTime + compressionInterval < value.CloseTime || needToAddCurrentAsSingle || i == 0))
            //    {
            //        if (processingCount > 0)
            //        {
            //            AddValueFromLists(summary);
            //            ClearLists();
            //            summary.OpenTime = openTime;
            //            summary.CloseTime = addingCurrent ? value.CloseTime : (values[i + 1] as BarBaseValue).CloseTime;
            //            result.Add(Convert(summary));
            //            summary = new SummaryBarItem<int>() { Count = 0, Max = int.MinValue, Min = int.MaxValue };
            //            processingCount = 0;
            //        }
            //    }

            //    //We add current bar to list if needed, and proceed to the next one
            //    if (needToAddCurrentAsSingle)
            //    {
            //        result.Add(value);
            //        needToAddCurrentAsSingle = false;
            //        if (i != 0)
            //        {
            //            openTime = (values[i - 1] as BarBaseValue).OpenTime;
            //            continue;
            //        }
            //    }

            //    //Start new bar, might need this right after finished previous
            //    //We start new bar if we finished previous and there are more objects in the list
            //    //We continue after starting because we have already processed it
            //    if (processingCount == 0 && i != 0)
            //    {
            //        openTime = value.OpenTime;
            //        AddValueToList(value);
            //        ProcessItem(value, summary);
            //        ++processingCount;
            //        continue;
            //    }

            //    //If we did not finish previous bar and did not add current, just add currently processed bar and continue
            //    AddValueToList(value);
            //    ProcessItem(value, summary);
            //    ++processingCount;
            //}

            return result;
        }

        public override string GetCsvHistory(List<BaseValue> values)
        {
            var sb = new StringBuilder(values.Count);

            sb.AppendLine($"Index,StartTime,EndTime,Min,Max,Mean,Count,Last");
            for (int i = 0; i < values.Count; ++i)
            {
                if (values[i] is IntegerBarValue value)
                    sb.AppendLine($"{i},{value.OpenTime.ToUniversalTime():s},{value.CloseTime.ToUniversalTime():s},{value.Min},{value.Max},{value.Mean},{value.Count},{value.LastValue}");
            }

            return sb.ToString();
        }

        private void AddValueToList(IntegerBarValue value)
        {
            try
            {
                _MeanList.Add(new KeyValuePair<int, int>(value.Mean, value.Count));
            }
            catch { }
        }

        /// <summary>
        /// Set fields, for which collecting lists of values is required
        /// </summary>
        /// <param name="summary"></param>
        private void AddValueFromLists(SummaryBarItem<int> summary)
        {
            summary.Mean = CountMean(_MeanList);
        }

        private void ClearLists()
        {
            _MeanList.Clear();
        }

        private IntegerBarValue Convert(SummaryBarItem<int> summary)
        {
            var result = new IntegerBarValue()
            {
                Count = summary.Count,
                OpenTime = summary.OpenTime,
                CloseTime = summary.CloseTime,
                Min = summary.Min,
                Max = summary.Max,
                Mean = CountMean(_MeanList),//summary.Mean,
                Time = summary.CloseTime,
            };

            ClearLists();

            return result;
        }

        /// <summary>
        /// This method applies possible changes to the current data item for fields, for which collecting datas is not required
        /// </summary>
        /// <param name="value">Currently processed data item</param>
        /// <param name="summary">Current summary item</param>
        private void ProcessItem(IntegerBarValue value, SummaryBarItem<int> summary)
        {
            summary.CloseTime = value.CloseTime;
            summary.Count += value.Count;

            if (value.Max > summary.Max)
                summary.Max = value.Max;

            if (value.Min < summary.Min)
                summary.Min = value.Min;

            AddValueToList(value);
        }

        /// <summary>
        /// Count mean from the list of all means
        /// </summary>
        /// <param name="means"></param>
        /// <returns></returns>
        private static int CountMean(List<KeyValuePair<int, int>> means)
        {
            if (means.Count < 1)
                return 0;

            decimal sum = 0;
            int commonCount = 0;
            foreach (var meanPair in means)
            {
                sum += meanPair.Key * meanPair.Value;
                commonCount += meanPair.Value;
            }

            if (commonCount < 1)
                return 0;

            return (int)(sum / commonCount);
        }
    }
}