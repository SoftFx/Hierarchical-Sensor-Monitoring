using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;

namespace HSMDataCollector.Bar
{
    public class BarSensorInt : BarSensorBase, IIntBarSensor
    {
        private readonly SortedSet<int> ValuesList;
        public BarSensorInt(string path, string productKey, string serverAddress, IValuesQueue queue, int collectPeriod = 300000,
            int smallPeriod = 15000)
            : this(path, productKey, $"{serverAddress}/intBar", queue, TimeSpan.FromMilliseconds(collectPeriod),
                TimeSpan.FromMilliseconds(smallPeriod))
        {
            
        }

        public BarSensorInt(string path, string productKey, string serverAddress, IValuesQueue queue,
            TimeSpan collectPeriod,
            TimeSpan smallPeriod) : base(path, productKey, $"{serverAddress}/doubleBar", queue, collectPeriod,
            smallPeriod)
        {
            ValuesList = new SortedSet<int>(new DuplicateIntComparer());
        }

        public override CommonSensorValue GetLastValue()
        {
            try
            {
                IntBarSensorValue dataObject = GetDataObject();
                CommonSensorValue commonValue = ToCommonSensorValue(dataObject);
                return commonValue;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        protected override void SendDataTimer(object state)
        {
            IntBarSensorValue dataObject = GetDataObject();
            CommonSensorValue commonValue = ToCommonSensorValue(dataObject);
            SendData(commonValue);
        }

        protected override void SmallTimerTick(object state)
        {
            IntBarSensorValue dataObject;
            try
            {
                dataObject = GetPartialDataObject();
            }
            catch (Exception e)
            {
                return;
            }
            CommonSensorValue commonValue = ToCommonSensorValue(dataObject);
            SendData(commonValue);
        }

        public void AddValue(int value)
        {
            lock (_syncObject)
            {
                ValuesList.Add(value);
            }
        }
        private CommonSensorValue ToCommonSensorValue(IntBarSensorValue data)
        {
            CommonSensorValue result = new CommonSensorValue();
            string serializedValue = GetStringData(data);
            result.TypedValue = serializedValue;
            result.SensorType = SensorType.IntegerBarSensor;
            return result;
        }

        private IntBarSensorValue GetPartialDataObject()
        {
            IntBarSensorValue result = new IntBarSensorValue();
            List<int> currentValues;
            lock (_syncObject)
            {
                currentValues = new List<int>(ValuesList);

                result.StartTime = barStart;
            }

            FillNumericData(result, currentValues);
            FillCommonData(result);
            result.EndTime = DateTime.MinValue;

            return result;
        }
        private IntBarSensorValue GetDataObject()
        {
            IntBarSensorValue result = new IntBarSensorValue();
            List<int> collected;
            lock (_syncObject)
            {
                collected = new List<int>(ValuesList);
                ValuesList.Clear();

                //New bar starts right after the previous one ends
                result.StartTime = barStart;
                result.EndTime = DateTime.Now;
                barStart = DateTime.Now;
            }

            FillNumericData(result, collected);

            FillCommonData(result);
            return result;
        }

        private void FillNumericData(IntBarSensorValue value, List<int> values)
        {
            value.Max = values.Last();
            value.Min = values.First();
            value.Count = values.Count;
            value.Mean = CountMean(values);
            value.Percentiles.Add(new PercentileValueInt(GetPercentile(values, 0.25), 0.25));
            value.Percentiles.Add(new PercentileValueInt(GetPercentile(values, 0.5), 0.5));
            value.Percentiles.Add(new PercentileValueInt(GetPercentile(values, 0.75), 0.75));
        }

        private void FillCommonData(IntBarSensorValue value)
        {
            value.Key = ProductKey;
            value.Path = Path;
            value.Time = DateTime.Now;
        }

        protected override string GetStringData(SensorValueBase data)
        {
            try
            {
                IntBarSensorValue typedData = (IntBarSensorValue)data;
                return JsonSerializer.Serialize(typedData);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return string.Empty;
            }
        }

        protected override byte[] GetBytesData(SensorValueBase data)
        {
            try
            {
                IntBarSensorValue typedData = (IntBarSensorValue)data;
                string convertedString = JsonSerializer.Serialize(typedData);
                return Encoding.UTF8.GetBytes(convertedString);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return new byte[1];
            }
            
        }

        private int CountMean(List<int> values)
        {
            long sum = values.Sum();
            int mean = 0;
            try
            {
                mean = (int)(sum / values.Count);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }

            return mean;
        }

        private class DuplicateIntComparer : IComparer<int>
        {
            public int Compare(int x, int y)
            {
                int result = x.CompareTo(y);

                //Handle equity as greater
                return result == 0 ? 1 : result;
            }
        }
    }
}
