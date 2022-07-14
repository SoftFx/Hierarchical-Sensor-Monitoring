using HSMServer.Core.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal class DoubleBarHistoryProcessor : HistoryProcessorBase
    {
        private readonly NumberFormatInfo _format;
        private readonly List<KeyValuePair<double, int>> _MeanList = new();


        public DoubleBarHistoryProcessor()
        {
            _format = new NumberFormatInfo();
            _format.NumberDecimalSeparator = ".";
        }


        protected override List<BaseValue> ProcessHistory(List<BaseValue> values, TimeSpan compressionInterval)
        {
            if (values == null || values.Count == 0)
                return new();

            if (values.Count == 1)
                return values;

            var result = new List<BaseValue>();

            var initTime = (values.First() as BarBaseValue).OpenTime;
            var summary = new SummaryBarItem<double>() { Count = 0, Max = int.MinValue, Min = int.MaxValue, OpenTime = initTime, CloseTime = initTime };
            var nextBarTime = initTime + compressionInterval;

            for (int i = 0; i < values.Count; ++i)
            {
                if (values[i] is not DoubleBarValue value || value.CloseTime == DateTime.MinValue)
                    continue;

                if (summary.CloseTime + (value.CloseTime - value.OpenTime) <= nextBarTime)
                {
                    ProcessItem(value, summary);
                }
                else
                {
                    result.Add(Convert(summary));
                    summary = new SummaryBarItem<double>() { Count = value.Count, Max = value.Max, Min = value.Min, OpenTime = value.OpenTime, CloseTime = value.CloseTime };
                    while (nextBarTime <= summary.CloseTime)
                        nextBarTime += compressionInterval;
                }
            }

            result.Add(Convert(summary));


            //var result = new List<BaseValue>();

            //var summary = new SummaryBarItem<double>() { Count = 0, Max = double.MinValue, Min = double.MaxValue };
            //var openTime = (values.Last() as BarBaseValue).OpenTime;
            //int processingCount = 0;
            //bool needToAddCurrentAsSingle = false;
            //bool addingCurrent = false;

            //for (int i = values.Count - 1; i >= 0; --i)
            //{
            //    if (values[i] is not DoubleBarValue value)
            //        continue;

            //    if (value.CloseTime - value.OpenTime > compressionInterval ||
            //        (processingCount > 0 && openTime + compressionInterval < value.CloseTime && i == 0))
            //    {
            //        needToAddCurrentAsSingle = true;
            //    }

            //    if (value.CloseTime < openTime + compressionInterval && i == 0)
            //    {
            //        AddValueToList(value);
            //        ProcessItem(value, summary);
            //        addingCurrent = true;
            //    }

            //    if (i < values.Count - 1 && (openTime + compressionInterval < value.CloseTime || needToAddCurrentAsSingle || i == 0))
            //    {
            //        if (processingCount > 0)
            //        {
            //            AddValueFromLists(summary);
            //            ClearLists();
            //            summary.OpenTime = openTime;
            //            summary.CloseTime = addingCurrent ? value.CloseTime : (values[i + 1] as BarBaseValue).CloseTime;
            //            result.Add(Convert(summary));
            //            summary = new SummaryBarItem<double>() { Count = 0, Max = double.MinValue, Min = double.MaxValue };
            //            processingCount = 0;
            //        }
            //    }

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

            //    if (processingCount == 0 && i != 0)
            //    {
            //        openTime = value.OpenTime;
            //        AddValueToList(value);
            //        ProcessItem(value, summary);
            //        ++processingCount;
            //        continue;
            //    }

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
                if (values[i] is DoubleBarValue value)
                    sb.AppendLine($"{i},{value.OpenTime.ToUniversalTime():s},{value.CloseTime.ToUniversalTime():s},{value.Min.ToString(_format)}," +
                                  $"{value.Max.ToString(_format)},{value.Mean.ToString(_format)},{value.Count},{value.LastValue.ToString(_format)}");
            }

            return sb.ToString();
        }

        private void AddValueToList(DoubleBarValue value)
        {
            try
            {
                _MeanList.Add(new KeyValuePair<double, int>(value.Mean, value.Count));
            }
            catch { }
        }

        private void AddValueFromLists(SummaryBarItem<double> summary)
        {
            summary.Mean = CountMean(_MeanList);
        }

        private void ClearLists()
        {
            _MeanList.Clear();
        }

        private static DoubleBarValue Convert(SummaryBarItem<double> summary) =>
          new()
          {
              Count = summary.Count,
              OpenTime = summary.OpenTime,
              CloseTime = summary.CloseTime,
              Min = summary.Min,
              Max = summary.Max,
              Mean = summary.Mean,
              Time = summary.CloseTime,
          };

        private static void ProcessItem(DoubleBarValue value, SummaryBarItem<double> summary)
        {
            summary.Count += value.Count;
            if (value.Max > summary.Max)
                summary.Max = value.Max;

            if (value.Min < summary.Min)
                summary.Min = value.Min;
        }

        private static double CountMean(List<KeyValuePair<double, int>> means)
        {
            if (means.Count < 1)
                return 0.0;

            double sum = 0.0;
            int commonCount = 0;
            foreach (var meanPair in means)
            {
                sum += meanPair.Key * meanPair.Value;
                commonCount += meanPair.Value;
            }

            if (commonCount < 1)
                return 0.0;

            return sum / commonCount;
        }
    }
}
