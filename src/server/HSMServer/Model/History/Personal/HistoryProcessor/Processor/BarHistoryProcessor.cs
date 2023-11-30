using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace HSMServer.Model.History
{
    internal abstract class BarHistoryProcessor<T> : HistoryProcessorBase where T : struct, INumber<T>, IComparable
    {
        private readonly List<(T, int)> _meanList = new();


        protected abstract T DefaultMax { get; }

        protected abstract T DefaultMin { get; }


        protected abstract BarBaseValue<T> GetBarValue(SummaryBarItem<T> summary);

        protected abstract double GetComposition(T value1, int value2);

        protected abstract T Convert(double value);

        protected abstract T Average(T value1, T value2);


        protected override List<BaseValue> Compress(List<BaseValue> values, TimeSpan compressionInterval)
        {
            if (values == null || values.Count == 0)
                return new();

            var result = new List<BaseValue>();

            var oldestValue = values.First() as BarBaseValue<T>;
            DateTime nextBarTime = oldestValue.OpenTime + compressionInterval;

            SummaryBarItem<T> summary = new(oldestValue.OpenTime, oldestValue.CloseTime, DefaultMax, DefaultMin, oldestValue.FirstValue, oldestValue.LastValue);
            ProcessItem(oldestValue, summary);

            for (int i = 1; i < values.Count; ++i)
            {
                if (values[i] is not BarBaseValue<T> value || value.CloseTime == DateTime.MinValue)
                    continue;

                if (summary.CloseTime + (value.CloseTime - value.OpenTime) > nextBarTime)
                {
                    result.Add(Convert(summary, summary.Count != value.Count));

                    summary = new(value.OpenTime, value.CloseTime, DefaultMax, DefaultMin, oldestValue.FirstValue, oldestValue.LastValue);
                    ProcessItem(value, summary);

                    while (nextBarTime <= summary.CloseTime)
                        nextBarTime += compressionInterval;
                }
                else
                    ProcessItem(value, summary);
            }

            result.Add(Convert(summary, (values[^1] as BarBaseValue).Count != summary.Count));

            return result;
        }

        private void AddValueToList(BarBaseValue<T> value)
        {
            try
            {
                _meanList.Add((value.Mean, value.Count));
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
        }

        private void ClearLists()
        {
            _meanList.Clear();
        }

        private BarBaseValue<T> Convert(SummaryBarItem<T> summary, bool isCompressed = true)
        {
            AddValueFromLists(summary);

            var result = !isCompressed ? new NotCompressedValue<T>(GetBarValue(summary)) : GetBarValue(summary);

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

            if (summary.Count == 0)
                summary.FirstValue = value.FirstValue;

            summary.LastValue = value.LastValue;
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

            double sum = 0;
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
    }
}
