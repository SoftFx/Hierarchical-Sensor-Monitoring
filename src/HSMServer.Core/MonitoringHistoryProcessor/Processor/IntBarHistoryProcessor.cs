using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.TypedDataObject;
using HSMServer.Core.Model.Sensor;
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
        private readonly List<KeyValuePair<int, int>> _MeanList = new List<KeyValuePair<int, int>>();

        public IntBarHistoryProcessor()
        {

        }
        public IntBarHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        protected override List<SensorHistoryData> ProcessHistoryInternal(List<SensorHistoryData> uncompressedData,
            TimeSpan compressionInterval)
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
                //We must add current processed object if its period bigger than interval,
                //or if current object is longer than than interval, or if we are processing the last object
                if (typedDatas[i].EndTime - typedDatas[i].StartTime > compressionInterval ||
                    (processingCount > 0 && startDate + compressionInterval < typedDatas[i].EndTime
                                         && i == typedDatas.Count - 1))
                {
                    needToAddCurrentAsSingle = true;
                }

                //Just process current bar as usual if it is not the last & in the interval
                if (typedDatas[i].EndTime < startDate + compressionInterval && i == typedDatas.Count - 1)
                {
                    AddDataToList(typedDatas[i]);
                    ProcessItem(typedDatas[i], currentItem);
                    addingCurrent = true;
                }

                //Finish bar if necessary. We finish previous bar if we are adding current as single
                //or if we are processing the last bar or if next bar is not in the interval
                if (i > 0 && (startDate + compressionInterval < typedDatas[i].EndTime || needToAddCurrentAsSingle
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

                //We add current bar to list if needed, and proceed to the next one
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
                //We start new bar if we finished previous and there are more objects in the list
                //We continue after starting because we have already processed it
                if (processingCount == 0 && i != typedDatas.Count - 1)
                {
                    startDate = typedDatas[i].StartTime;
                    AddDataToList(typedDatas[i]);
                    ProcessItem(typedDatas[i], currentItem);
                    ++processingCount;
                    continue;
                }
                
                //If we did not finish previous bar and did not add current, just add currently processed bar
                // and continue
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

        /// <summary>
        /// This method applies possible changes to the current data item for fields, for which
        /// collecting datas is not required
        /// </summary>
        /// <param name="data">Currently processed data item</param>
        /// <param name="currentItem">Current summary item</param>
        private void ProcessItem(IntBarSensorData data, IntBarSensorData currentItem)
        {
            currentItem.Count += data.Count;
            if (data.Max > currentItem.Max)
                currentItem.Max = data.Max;

            if (data.Min < currentItem.Min)
                currentItem.Min = data.Min;
        }

        /// <summary>
        /// Add all percentiles to united percentile list for later calculations of Q1, median and Q3
        /// </summary>
        /// <param name="data"></param>
        private void AddDataToList(IntBarSensorData data)
        {
            try
            {
                _MeanList.Add(new KeyValuePair<int, int>(data.Mean, data.Count));
                if (data.Percentiles != null && data.Percentiles.Any())
                    _percentilesList.AddRange(data.Percentiles.Select(p => p.Value));
            }
            catch (Exception e)
            {
                
            }
        }

        /// <summary>
        /// Set fields, for which collecting lists of values is required
        /// </summary>
        /// <param name="currentItem"></param>
        private void AddDataFromLists(IntBarSensorData currentItem)
        {
            currentItem.Mean = CountMean(_MeanList);
            currentItem.Percentiles = new List<PercentileValueInt>();
            //Just add values that "seem to be fine" if there is no more data
            if (_percentilesList.Count < 3)
            {
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.5, Value = currentItem.Mean });
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.25, Value = currentItem.Min });
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.75, Value = currentItem.Max });
                return;
            }

            _percentilesList.Sort();
            //Special case where Q1 and Q3 calculations may fail
            if (_percentilesList.Count == 3)
            {
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.5, Value = _percentilesList[1] });
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.25, Value = _percentilesList[0] });
                currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.75, Value = _percentilesList[2] });
                return;
            }

            //Calculate all percentiles normally
            currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.5, Value = CountMedian() });
            currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.25, Value = CountQ1() });
            currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.75, Value = CountQ3() });
        }
        
        /// <summary>
        /// Count mean from the list of all means
        /// </summary>
        /// <param name="means"></param>
        /// <returns></returns>
        private int CountMean(List<KeyValuePair<int, int>> means)
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

        /// <summary>
        /// Get median for the list of values, use average for odd index
        /// </summary>
        /// <returns>median from the percentiles list</returns>
        private int CountMedian()
        {
            if (_percentilesList.Count % 2 == 1)
            {
                return _percentilesList[(_percentilesList.Count - 1) / 2];
            }

            var ind = _percentilesList.Count / 2;
            return (_percentilesList[ind - 1] + _percentilesList[ind]) / 2;
        }

        /// <summary>
        /// Get Q1 for the list of values, use average for odd index
        /// </summary>
        /// <returns>Q1 from the percentiles list</returns>
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
        }

        /// <summary>
        /// Get Q3 for the list of values, use average for odd index
        /// </summary>
        /// <returns>Q3 from the percentiles list</returns>
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