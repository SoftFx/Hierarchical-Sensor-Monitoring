using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMDataCollector.Bar
{
    [Obsolete("08.07.2021. Use BarSensor.")]
    public class BarSensorInt : BarSensorBase, IIntBarSensor
    {
        private readonly List<int> _valuesList;
        
        public BarSensorInt(string path, string productKey, IValuesQueue queue,
            int collectPeriod = 300000,
            int smallPeriod = 15000) : base(path, productKey, queue, collectPeriod,
            smallPeriod, "")
        {
            _valuesList = new List<int>();
        }

        public override CommonSensorValue GetLastValue()
        {
            try
            {
                IntBarSensorValue dataObject = GetPartialDataObject();
                CommonSensorValue commonValue = ToCommonSensorValue(dataObject);
                return commonValue;
            }
            catch (Exception e)
            {
                return null;
            }
        }

        public override SensorValueBase GetLastValueNew()
        {
            throw new NotImplementedException();
        }

        protected override void SendDataTimer(object state)
        {
            IntBarSensorValue dataObject = GetDataObject();
            CommonSensorValue commonValue = ToCommonSensorValue(dataObject);
            EnqueueData(commonValue);
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
            EnqueueData(commonValue);
        }

        public void AddValue(int value)
        {
            lock (_syncObject)
            {
                _valuesList.Add(value);
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
                currentValues = new List<int>(_valuesList);

                result.StartTime = barStart;
            }

            result.LastValue = currentValues.Last();
            currentValues.Sort();
            FillNumericData(result, currentValues);
            FillCommonData(result);
            result.EndTime = DateTime.MinValue.ToUniversalTime();

            return result;
        }
        private IntBarSensorValue GetDataObject()
        {
            IntBarSensorValue result = new IntBarSensorValue();
            List<int> collected;
            lock (_syncObject)
            {
                collected = new List<int>(_valuesList);
                _valuesList.Clear();

                //New bar starts right after the previous one ends
                result.StartTime = barStart;
                result.EndTime = DateTime.Now;
                barStart = DateTime.Now;
            }

            result.LastValue = collected.Last();
            collected.Sort();
            FillNumericData(result, collected);

            FillCommonData(result);
            return result;
        }

        private void FillNumericData(IntBarSensorValue value, List<int> values)
        {
            if (values.Any())
            {
                value.Max = values.Last();
                value.Min = values.First();
            }
            else
            {
                value.Max = 0;
                value.Min = 0;
            }
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
                return JsonConvert.SerializeObject(typedData);
                //return Serializer.Serialize(typedData);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
                return string.Empty;
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
