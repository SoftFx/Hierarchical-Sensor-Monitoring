using HSMCommon.Model.SensorsData;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.TypedDataObject;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace HSMServer.Core.MonitoringHistoryProcessor.Processor
{
    internal class IntBarHistoryProcessor : HistoryProcessorBase
    {
        //private readonly List<int> _Q1List = new List<int>();
        //private readonly List<int> _Q3List = new List<int>();
        //private readonly List<int> _MedianList = new List<int>();
        private readonly List<int> _percentilesList = new List<int>();
        private readonly List<int> _MeanList = new List<int>();
        public IntBarHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        {
            if(uncompressedData == null || !uncompressedData.Any())
                return new List<SensorHistoryData>();

            if (uncompressedData.Count == 1)
                return uncompressedData;

            List<IntBarSensorData> typedDatas = GetTypeDatas(uncompressedData);
            List<SensorHistoryData> result = new List<SensorHistoryData>();
            IntBarSensorData currentItem = new IntBarSensorData() {Count = 0, Max = int.MinValue, Min = int.MaxValue};
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
                        currentItem = new IntBarSensorData() { Count = 0, Max = int.MinValue, Min = int.MaxValue };
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

        public override string GetCsvHistory(List<SensorHistoryData> originalData)
        {
            List<IntBarSensorData> typedDatas = GetTypeDatas(originalData);
            StringBuilder sb = new StringBuilder();
            sb.Append($"Index,StartTime,EndTime,Min,Max,Mean,Count,Last{Environment.NewLine}");
            for (int i = 0; i < typedDatas.Count; ++i)
            {
                sb.Append($"{i},{typedDatas[i].StartTime.ToUniversalTime():s},{typedDatas[i].EndTime.ToUniversalTime():s}" +
                          $",{typedDatas[i].Min},{typedDatas[i].Max},{typedDatas[i].Mean},{typedDatas[i].Count}," +
                          $"{typedDatas[i].LastValue}{Environment.NewLine}");
            }

            return sb.ToString();
        }

        private List<IntBarSensorData> GetTypeDatas(List<SensorHistoryData> uncompressedData)
        {
            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            List<IntBarSensorData> typedDatas = new List<IntBarSensorData>();
            foreach (var unProcessed in uncompressedData)
            {
                try
                {
                    typedDatas.Add(JsonSerializer.Deserialize<IntBarSensorData>(unProcessed.TypedData));
                }
                catch (Exception e)
                { }
            }

            return typedDatas;
        }
        private SensorHistoryData Convert(IntBarSensorData typedData)
        {
            SensorHistoryData result = new SensorHistoryData();
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.Time = typedData.EndTime;
            result.SensorType = SensorType.IntegerBarSensor;
            return result;
        }

        private void ProcessItem(IntBarSensorData data, IntBarSensorData currentItem)
        {
            currentItem.Count += data.Count;
            if (data.Max > currentItem.Max)
                currentItem.Max = data.Max;

            if (data.Min < currentItem.Min)
                currentItem.Min = data.Min;
        }

        private void AddDataToList(IntBarSensorData data)
        {
            try
            {
                _MeanList.Add(data.Mean);
                if (data.Percentiles != null && data.Percentiles.Any())
                    _percentilesList.AddRange(data.Percentiles.Select(p => p.Value));
            }
            catch (Exception e)
            {
                
            }
        }

        private void AddDataFromLists(IntBarSensorData currentItem)
        {
            currentItem.Mean = (int)(_MeanList.Sum() / _MeanList.Count == 0 ? 1 : _MeanList.Count);
            currentItem.Percentiles = new List<PercentileValueInt>();
            if (_percentilesList.Count < 3)
            {
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.5, Value = currentItem.Mean });
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.25, Value = currentItem.Min });
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.75, Value = currentItem.Max });
                return;
            }

            _percentilesList.Sort();
            if (_percentilesList.Count == 3)
            {
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.5, Value = _percentilesList[1] });
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.25, Value = _percentilesList[0] });
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.75, Value = _percentilesList[2] });
                return;
            }
            currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.5, Value = CountMedian() });
            currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.25, Value = CountQ1() });
            currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.75, Value = CountQ3() });
        }

        private int CountMedian()
        {
            if (_percentilesList.Count % 2 == 1)
            {
                return _percentilesList[(_percentilesList.Count - 1) / 2];
            }

            var ind = _percentilesList.Count / 2;
            return (_percentilesList[ind - 1] + _percentilesList[ind]) / 2;
        }

        private int CountQ1()
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
            //if (_percentilesList.Count % 2 == 0)
            //{
            //    int middle = _percentilesList.Count / 2;
            //    if (middle % 2 == 0)
            //    {
            //        int quart = middle / 2;
            //        return (_percentilesList[quart] + _percentilesList[quart - 1]) / 2;
            //    }

            //    return _percentilesList[(middle - 1) / 2];
            //}

            //int mid = (_percentilesList.Count + 1) / 2;
            //if (mid % 2 == 0)
            //{
            //    int quar = mid / 2;
            //    return (_percentilesList[quar] + _percentilesList[quar - 1]) / 2;
            //}

            //return _percentilesList[(mid - 1) / 2];
        }

        private int CountQ3()
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
        //private void AddData(IntBarSensorData currentItem,)
    }
}