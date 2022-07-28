using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal abstract class BarHistoryProcessor<T> : HistoryProcessorBase where T : struct, IComparable
    {
        private readonly List<(T, int)> _meanList = new();
        private readonly List<T> _percentilesList = new();


        protected abstract T DefaultMax { get; }

        protected abstract T DefaultMin { get; }


        protected abstract BarBaseValue<T> GetBarValue(SummaryBarItem<T> summary);

        protected abstract decimal GetComposition(T value1, int value2);

        protected abstract T Convert(decimal value);

        protected abstract T Average(T value1, T value2);


        public override string GetCsvHistory(List<BaseValue> values)
        {
            var sb = new StringBuilder(values.Count);

            sb.AppendLine($"Index,StartTime,EndTime,Min,Max,Mean,Count,Last");
            for (int i = 0; i < values.Count; ++i)
            {
                if (values[i] is BarBaseValue<T> value)
                    sb.AppendLine($"{i},{GetCsvRow(value)}");
            }

            return sb.ToString();
        }

        protected virtual string GetCsvRow(BarBaseValue<T> value) =>
            $"{value.OpenTime.ToUniversalTime():s},{value.CloseTime.ToUniversalTime():s},{value.Min},{value.Max},{value.Mean},{value.Count},{value.LastValue}";

        protected override List<BaseValue> ProcessHistory(List<BaseValue> values, TimeSpan compressionInterval)
        {
            if (values == null || values.Count == 0)
                return new();

            var result = new List<BaseValue>();

            var oldestValue = values.First() as BarBaseValue<T>;
            DateTime nextBarTime = oldestValue.OpenTime + compressionInterval;

            SummaryBarItem<T> summary = new(oldestValue.OpenTime, oldestValue.CloseTime, DefaultMax, DefaultMin);
            ProcessItem(oldestValue, summary);

            for (int i = 1; i < values.Count; ++i)
            {
                if (values[i] is not BarBaseValue<T> value || value.CloseTime == DateTime.MinValue)
                    continue;

                if (summary.CloseTime + (value.CloseTime - value.OpenTime) > nextBarTime)
                {
                    result.Add(Convert(summary));

                    summary = new(value.OpenTime, value.CloseTime, DefaultMax, DefaultMin);
                    ProcessItem(value, summary);

                    while (nextBarTime <= summary.CloseTime)
                        nextBarTime += compressionInterval;
                }
                else
                    ProcessItem(value, summary);
            }

            result.Add(Convert(summary));

            return result;
        }

        private void AddValueToList(BarBaseValue<T> value)
        {
            try
            {
                _meanList.Add((value.Mean, value.Count));

                if (value.Percentiles != null && value.Percentiles.Count > 0)
                    _percentilesList.AddRange(value.Percentiles.Select(p => p.Value));
            }
            catch { }
        }

        /// <summary>
        /// Set fields, for which collecting lists of values is required
        /// </summary>
        /// <param name="summary"></param>
        private void AddValueFromLists(SummaryBarItem<T> summary)
        {
            summary.Mean = CountMean(_meanList);
            summary.Percentiles = new();

            //Just add values that "seem to be fine" if there is no more data
            if (_percentilesList.Count < 3)
            {
                summary.Percentiles.Add(0.5, summary.Mean);
                summary.Percentiles.Add(0.25, summary.Min);
                summary.Percentiles.Add(0.75, summary.Max);
                return;
            }

            _percentilesList.Sort();

            //Special case where Q1 and Q3 calculations may fail
            if (_percentilesList.Count == 3)
            {
                summary.Percentiles.Add(0.5, _percentilesList[1]);
                summary.Percentiles.Add(0.25, _percentilesList[0]);
                summary.Percentiles.Add(0.75, _percentilesList[2]);
                return;
            }

            //Calculate all percentiles normally
            summary.Percentiles.Add(0.5, CountMedian());
            summary.Percentiles.Add(0.25, CountQ1());
            summary.Percentiles.Add(0.75, CountQ3());
        }

        private void ClearLists()
        {
            _meanList.Clear();
            _percentilesList.Clear();
        }

        private BarBaseValue<T> Convert(SummaryBarItem<T> summary)
        {
            AddValueFromLists(summary);

            var result = GetBarValue(summary);

            ClearLists();

            return result;
        }

        /// <summary>
        /// This method applies possible changes to the current data item for fields, for which collecting datas is not required
        /// </summary>
        /// <param name="value">Currently processed data item</param>
        /// <param name="summary">Current summary item</param>
        private void ProcessItem(BarBaseValue<T> value, SummaryBarItem<T> summary)
        {
            AddValueToList(value);

            summary.CloseTime = value.CloseTime;
            summary.Count += value.Count;

            if (value.Max.CompareTo(summary.Max) > 0)
                summary.Max = value.Max;

            if (value.Min.CompareTo(summary.Min) < 0)
                summary.Min = value.Min;
        }

        /// <summary>
        /// Count mean from the list of all means
        /// </summary>
        /// <param name="means"></param>
        /// <returns></returns>
        private T CountMean(List<(T mean, int count)> means)
        {
            if (means.Count < 1)
                return default;

            decimal sum = 0;
            int commonCount = 0;
            foreach (var meanPair in means)
            {
                sum += GetComposition(meanPair.mean, meanPair.count);
                commonCount += meanPair.count;
            }

            if (commonCount < 1)
                return default;

            return Convert(sum / commonCount);
        }

        /// <returns>median from the percentiles list</returns>
        private T CountMedian()
        {
            var index = _percentilesList.Count - 1;

            int left = index / 2;
            int right = left + (index % 2);

            return Average(_percentilesList[left], _percentilesList[right]);
        }

        /// <returns>Q1 from the percentiles list</returns>
        private T CountQ1()
        {
            int left = _percentilesList.Count / 4;
            int right = left + (_percentilesList.Count % 2);

            return Average(_percentilesList[left], _percentilesList[right]);
        }

        /// <returns>Q3 from the percentiles list</returns>
        private T CountQ3()
        {
            int left = (3 * _percentilesList.Count - 2) / 4;
            int right = left + (_percentilesList.Count % 2);

            return Average(_percentilesList[left], _percentilesList[right]);
        }
    }
}
