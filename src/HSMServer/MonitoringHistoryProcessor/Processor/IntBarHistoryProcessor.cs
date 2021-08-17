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
    internal class IntBarHistoryProcessor : HistoryProcessorBase
    {
        private List<int> _Q1List = new List<int>();
        private List<int> _Q3List = new List<int>();
        private List<int> _MedianList = new List<int>();
        private List<int> _MeanList = new List<int>();
        public IntBarHistoryProcessor(TimeSpan periodInterval) : base(periodInterval)
        {
        }

        public override List<SensorHistoryData> ProcessHistory(List<SensorHistoryData> uncompressedData)
        {
            if(uncompressedData == null || !uncompressedData.Any())
                return new List<SensorHistoryData>();

            uncompressedData.Sort((d1, d2) => d1.Time.CompareTo(d2.Time));
            List<SensorHistoryData> result = new List<SensorHistoryData>();
            IntBarSensorData currentItem = new IntBarSensorData();
            DateTime startDate = uncompressedData[0].Time;
            for (int i = 0; i < uncompressedData.Count; ++i)
            {
                IntBarSensorData typedData = JsonSerializer.Deserialize<IntBarSensorData>(uncompressedData[i].TypedData);
                if (uncompressedData[i].Time > startDate + PeriodInterval || i == uncompressedData.Count - 1)
                {
                    AddDataFromLists(currentItem);
                    currentItem.StartTime = startDate;
                    currentItem.EndTime = startDate + PeriodInterval;
                    ClearLists();
                    result.Add(Convert(currentItem, startDate + PeriodInterval));
                    currentItem = new IntBarSensorData();
                    currentItem.Min = int.MaxValue;
                    currentItem.Max = int.MinValue;
                    //TODO: count intervals properly
                    startDate += PeriodInterval;
                }

                ProcessItem(typedData, currentItem);
                AddDataToList(typedData);
            }

            return result;
        }

        private SensorHistoryData Convert(IntBarSensorData typedData, DateTime time)
        {
            SensorHistoryData result = new SensorHistoryData();
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.Time = time;
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
            _MeanList.Add(data.Mean);
            var median = data.Percentiles.FirstOrDefault(med => Math.Abs(med.Percentile - 0.5) < double.Epsilon);
            if (median != null)
                _MedianList.Add(median.Value);

            var q1 = data.Percentiles.FirstOrDefault(q => Math.Abs(q.Percentile - 0.25) < double.Epsilon);
            if (q1 != null)
                _Q1List.Add(q1.Value);

            var q3 = data.Percentiles.FirstOrDefault(q => Math.Abs(q.Percentile - 0.75) < double.Epsilon);
            if (q3 != null)
                _Q3List.Add(q3.Value);
        }

        private void AddDataFromLists(IntBarSensorData currentItem)
        {
            currentItem.Mean = (int)(_MeanList.Sum() / currentItem.Count);
            currentItem.Percentiles = new List<PercentileValueInt>();
            currentItem.Percentiles.Add(new PercentileValueInt() {Percentile = 0.5, Value = _MedianList[(int)(_MedianList.Count / 2)]});
            currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.25, Value = _Q1List.Min() });
            currentItem.Percentiles.Add(new PercentileValueInt() { Percentile = 0.75, Value = _Q3List.Max() });
        }

        private void ClearLists()
        {
            _Q1List.Clear();
            _Q3List.Clear();
            _MedianList.Clear();
            _MeanList.Clear();
        }
        //private void AddData(IntBarSensorData currentItem,)
    }
}