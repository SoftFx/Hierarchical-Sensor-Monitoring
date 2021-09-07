using HSMCommon.Model.SensorsData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.TypedDataObject;

namespace HSMServer.MonitoringHistoryProcessor.Processor
{
    internal class DoubleBarHistoryProcessor : HistoryProcessorBase
    {
        //private readonly List<double> _Q1List = new List<double>();
        //private readonly List<double> _Q3List = new List<double>();
        //private readonly List<double> _MedianList = new List<double>();
        private readonly List<double> _percentilesList = new List<double>();
        private readonly List<double> _MeanList = new List<double>();
        public DoubleBarHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        {
            if (uncompressedData == null || !uncompressedData.Any())
                return new List<SensorHistoryData>();

            if (uncompressedData.Count == 1)
                return uncompressedData;

            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            List<DoubleBarSensorData> typedDatas = new List<DoubleBarSensorData>();
            foreach (var unProcessed in uncompressedData)
            {
                try
                {
                    typedDatas.Add(JsonSerializer.Deserialize<DoubleBarSensorData>(unProcessed.TypedData));
                }
                catch (Exception e)
                { }
            }

            List<SensorHistoryData> result = new List<SensorHistoryData>();
            DoubleBarSensorData currentItem = new DoubleBarSensorData() { Count = 0, Max = double.MinValue, Min = double.MaxValue };
            DateTime startDate = typedDatas[0].StartTime;
            int processingCount = 0;
            bool needToAddCurrentAsSingle = false;
            bool addingCurrent = false;
            for (int i = 0; i < typedDatas.Count; ++i)
            {
                if (typedDatas[i].EndTime - typedDatas[i].StartTime > PeriodInterval ||
                    (processingCount > 0 && startDate + PeriodInterval < typedDatas[i].EndTime
                                         && i == typedDatas.Count - 1))
                {
                    needToAddCurrentAsSingle = true;
                }

                if (typedDatas[i].EndTime < startDate + PeriodInterval && i == typedDatas.Count - 1)
                {
                    AddDataToList(typedDatas[i]);
                    ProcessItem(typedDatas[i], currentItem);
                    addingCurrent = true;
                }
                //Finish bar if necessary
                if (i > 0 && (startDate + PeriodInterval < typedDatas[i].EndTime || needToAddCurrentAsSingle
                    || i == typedDatas.Count - 1))
                {
                    if (processingCount > 0)
                    {
                        AddDataFromLists(currentItem);
                        ClearLists();
                        currentItem.StartTime = startDate;
                        currentItem.EndTime = addingCurrent ? typedDatas[i].EndTime : typedDatas[i - 1].EndTime;
                        result.Add(Convert(currentItem));
                        currentItem = new DoubleBarSensorData() { Count = 0, Max = double.MinValue, Min = double.MaxValue };
                        processingCount = 0;
                    }
                }

                if (needToAddCurrentAsSingle)
                {
                    result.Add(Convert(typedDatas[i]));
                    needToAddCurrentAsSingle = false;
                    if (i != typedDatas.Count - 1)
                    {
                        startDate = typedDatas[i + 1].StartTime;
                        continue;
                    }
                }

                //if (i == typedDatas.Count - 1)
                //{
                //    result.Add(Convert(typedDatas[i], typedDatas[i].EndTime));
                //}

                //Start new bar, might need this right after finished previous
                if (processingCount == 0 && i != typedDatas.Count - 1)
                {
                    startDate = typedDatas[i].StartTime;
                    AddDataToList(typedDatas[i]);
                    ProcessItem(typedDatas[i], currentItem);
                    ++processingCount;
                    continue;
                }

                AddDataToList(typedDatas[i]);
                ProcessItem(typedDatas[i], currentItem);
                ++processingCount;
            }
            return result;
        }

        private SensorHistoryData Convert(DoubleBarSensorData typedData)
        {
            SensorHistoryData result = new SensorHistoryData();
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.Time = typedData.EndTime;
            result.SensorType = SensorType.DoubleBarSensor;
            return result;
        }

        private void ProcessItem(DoubleBarSensorData data, DoubleBarSensorData currentItem)
        {
            currentItem.Count += data.Count;
            if (data.Max > currentItem.Max)
                currentItem.Max = data.Max;

            if (data.Min < currentItem.Min)
                currentItem.Min = data.Min;
        }

        private void AddDataToList(DoubleBarSensorData data)
        {
            try
            {
                _MeanList.Add(data.Mean);
                //var median = data.Percentiles.FirstOrDefault(med => Math.Abs(med.Percentile - 0.5) < double.Epsilon);
                //if (median != null)
                //    _MedianList.Add(median.Value);

                //var q1 = data.Percentiles.FirstOrDefault(q => Math.Abs(q.Percentile - 0.25) < double.Epsilon);
                //if (q1 != null)
                //    _Q1List.Add(q1.Value);

                //var q3 = data.Percentiles.FirstOrDefault(q => Math.Abs(q.Percentile - 0.75) < double.Epsilon);
                //if (q3 != null)
                //    _Q3List.Add(q3.Value);
                _percentilesList.AddRange(data.Percentiles.Select(p => p.Value));
            }
            catch (Exception e)
            {
                
            }
        }

        private void AddDataFromLists(DoubleBarSensorData currentItem)
        {
            currentItem.Mean = _MeanList.Sum() / _MeanList.Count == 0 ? 1 : _MeanList.Count;
            currentItem.Percentiles = new List<PercentileValueDouble>();
            //var median = _MedianList[(int) (_MedianList.Count / 2)];
            if (_percentilesList.Count < 1)
            {
                currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.5, Value = 0.0 });
                currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.25, Value = 0.0 });
                currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.75, Value = 0.0 });
                return;
            }

            _percentilesList.Sort();
            if (_percentilesList.Count == 3)
            {
                currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.5, Value = _percentilesList[1] });
                currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.25, Value = _percentilesList[0] });
                currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.75, Value = _percentilesList[2] });
                return;
            }
            currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.5, Value = CountMedian() });
            currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.25, Value = CountQ1() });
            currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.75, Value = CountQ3() });
        }
        private double CountMedian()
        {
            if (_percentilesList.Count % 2 == 1)
            {
                return _percentilesList[(_percentilesList.Count - 1) / 2];
            }

            var ind = _percentilesList.Count / 2;
            return (_percentilesList[ind - 1] + _percentilesList[ind]) / 2;
        }

        private double CountQ1()
        {
            int middle = _percentilesList.Count % 2 == 0
                ? _percentilesList.Count / 2
                : (_percentilesList.Count + 1) / 2;

            if (middle % 2 == 0)
            {
                int quart = middle / 2;
                return (_percentilesList[quart] + _percentilesList[quart - 1]) / 2;
            }

            return _percentilesList[(middle - 1) / 2];
        }

        private double CountQ3()
        {
            int middle = _percentilesList.Count % 2 == 0
                ? _percentilesList.Count / 2
                : (_percentilesList.Count + 1) / 2;

            if (middle % 2 == 0)
            {
                int index = _percentilesList.Count % 2 == 0
                    ? (middle + _percentilesList.Count) / 2
                    : (middle + _percentilesList.Count + 1) / 2;
                return (_percentilesList[index] + _percentilesList[index + 1]) / 2;
            }

            return _percentilesList.Count % 2 == 0
                ? _percentilesList[(middle + _percentilesList.Count + 1) / 2]
                : _percentilesList[(middle + _percentilesList.Count) / 2];
        }
        private void ClearLists()
        {
            //_Q1List.Clear();
            //_Q3List.Clear();
            //_MedianList.Clear();
            _percentilesList.Clear();
            _MeanList.Clear();
        }
    }
}
