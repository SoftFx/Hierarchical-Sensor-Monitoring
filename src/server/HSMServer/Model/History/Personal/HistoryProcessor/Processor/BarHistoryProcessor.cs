using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using HSMCommon.Model;


namespace HSMServer.Model.History
{
    internal abstract class BarHistoryProcessor<T> : HistoryProcessorBase where T : struct, INumber<T>, IComparable
    {
        private readonly List<(T, int, bool)> _meanList = [];
        private readonly List<(int, double, double?)> _stdDevParts = [];


        protected abstract T DefaultMax { get; }

        protected abstract T DefaultMin { get; }


        protected abstract BarBaseValue<T> GetBarValue(SummaryBarItem<T> summary);

        protected abstract double GetComposition(T value1, int value2);

        protected abstract T Convert(double value);

        protected abstract T Average(T value1, T value2);


        protected override List<BaseValue> Compress(List<BaseValue> values, TimeSpan compressionInterval)
        {
            if (values == null || values.Count == 0)
                return [];

            var oldestValue = values.First() as BarBaseValue<T>;

            if (oldestValue is null)
                return [];

            var result = new List<BaseValue>();

            DateTime nextBarTime = oldestValue.OpenTime + compressionInterval;

            SummaryBarItem<T> summary = new(oldestValue.OpenTime, oldestValue.CloseTime, DefaultMax, DefaultMin, oldestValue.FirstValue, oldestValue.LastValue);
            ProcessItem(oldestValue, summary, out var isCompressed);

            for (int i = 1; i < values.Count; ++i)
            {
                if (values[i] is not BarBaseValue<T> value || value.CloseTime == DateTime.MinValue)
                    continue;

                if (summary.CloseTime + (value.CloseTime - value.OpenTime) > nextBarTime)
                {
                    result.Add(Convert(summary, isCompressed));

                    summary = new(value.OpenTime, value.CloseTime, DefaultMax, DefaultMin, oldestValue.FirstValue, oldestValue.LastValue);
                    ProcessItem(value, summary, out isCompressed);

                    while (nextBarTime <= summary.CloseTime)
                        nextBarTime += compressionInterval;
                }
                else
                    ProcessItem(value, summary, out isCompressed);
            }

            result.Add(Convert(summary, isCompressed));

            return result;
        }

        /// <summary>
        /// Set fields, for which collecting lists of values is required
        /// </summary>
        /// <param name="summary"></param>
        private void AddValueFromLists(SummaryBarItem<T> summary)
        {
            summary.Mean = CountMean(_meanList);
            summary.StdDev = BarStdDev.Combine(_stdDevParts);
        }

        private void ClearLists()
        {
            _meanList.Clear();
            _stdDevParts.Clear();
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
        /// <param name="IsCompressed">Current state of compression of summary</param>
        private void ProcessItem(BarBaseValue<T> value, SummaryBarItem<T> summary, out bool IsCompressed)
        {
            IsCompressed = summary.Count != 0;

            _meanList.Add((value.Mean, value.Count, value.IsTimeout));
            // A timeout row is a copy of the last bar (GetTimeoutValue), not new samples: counting
            // it would weigh that bar twice in the merged StdDev.
            if (!value.IsTimeout)
                _stdDevParts.Add((value.Count, double.CreateChecked(value.Mean), value.StdDev));

            if (!IsCompressed)
                summary.FirstValue = value.FirstValue;

            summary.LastValue = value.LastValue;
            summary.CloseTime = value.CloseTime;

            if (value.Count.CompareTo(summary.Count) > 0)
                summary.Count = value.Count;

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
        private T CountMean(List<(T mean, int count, bool isTimeout)> means)
        {
            // A timeout row repeats the last bar (GetTimeoutValue); weighing it again would pull the
            // mean away from the samples and off the centre of the StdDev band (#1509), which skips
            // timeout rows the same way. A bucket of timeout rows only keeps its old mean.
            if (means.Exists(m => !m.isTimeout))
                means = means.FindAll(m => !m.isTimeout);

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
