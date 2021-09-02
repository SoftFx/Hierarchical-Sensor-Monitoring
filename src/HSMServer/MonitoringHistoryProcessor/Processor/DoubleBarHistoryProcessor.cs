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
        private readonly List<double> _Q1List = new List<double>();
        private readonly List<double> _Q3List = new List<double>();
        private readonly List<double> _MedianList = new List<double>();
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
                {
                    //Console.WriteLine(e);
                }
            }

            //If bars interval is more than period, simply skip
            if (typedDatas[0].EndTime - typedDatas[0].StartTime > PeriodInterval)
                return uncompressedData;

            List<SensorHistoryData> result = new List<SensorHistoryData>();
            DoubleBarSensorData currentItem = new DoubleBarSensorData();
            DateTime startDate = typedDatas[0].StartTime;
            int processingCount = 0;
            for (int i = 0; i < typedDatas.Count; ++i)
            {
                //Finish bar if necessary
                if (i > 0 && (startDate + PeriodInterval < typedDatas[i].StartTime || i == typedDatas.Count - 1))
                {
                    if (processingCount < 1)
                        continue;

                    AddDataFromLists(currentItem);
                    ClearLists();
                    currentItem.StartTime = startDate;
                    currentItem.EndTime = typedDatas[i - 1].EndTime;
                    result.Add(Convert(currentItem, typedDatas[i - 1].EndTime));
                    currentItem = new DoubleBarSensorData();
                    processingCount = 0;
                }

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

        private SensorHistoryData Convert(DoubleBarSensorData typedData, DateTime time)
        {
            SensorHistoryData result = new SensorHistoryData();
            result.TypedData = JsonSerializer.Serialize(typedData);
            result.Time = time;
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
            catch (Exception e)
            {
                
            }
        }

        private void AddDataFromLists(DoubleBarSensorData currentItem)
        {
            currentItem.Mean = (int)(_MeanList.Sum() / _MeanList.Count == 0 ? 1 : _MeanList.Count);
            currentItem.Percentiles = new List<PercentileValueDouble>();
            var median = _MedianList[(int) (_MedianList.Count / 2)];
            currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.5, Value = median });
            //currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.25, Value = _Q1List[(int)(_Q1List.Count * 0.25)] });
            //currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.75, Value = _Q3List[(int)(_Q3List.Count * 0.75)] });
            currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.25, Value = (int)(median * 0.5) });
            currentItem.Percentiles.Add(new PercentileValueDouble() { Percentile = 0.75, Value = (int)(median * 1.5) });
        }

        private void ClearLists()
        {
            _Q1List.Clear();
            _Q3List.Clear();
            _MedianList.Clear();
            _MeanList.Clear();
        }
    }
}
