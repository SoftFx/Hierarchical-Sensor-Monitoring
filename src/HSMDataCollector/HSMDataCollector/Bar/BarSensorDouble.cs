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
    public class BarSensorDouble : BarSensorBase, IDoubleBarSensor
    {
        private readonly List<double> _valuesList;
        private readonly int _precision;

        public BarSensorDouble(string path, string productKey, IValuesQueue queue,
            int collectPeriod = 300000,
            int smallPeriod = 15000, int precision = 2) : base(path, productKey, queue, collectPeriod,
            smallPeriod)
        {
            _valuesList = new List<double>();
            if (precision < 1 || precision > 10)
            {
                _precision = 2;
            }
            else
            {
                _precision = precision;
            }
        }

        protected override void SmallTimerTick(object state)
        {
            DoubleBarSensorValue dataObject;
            try
            {
                dataObject = GetPartialDataObject();
            }
            catch (System.Exception e)
            {
                return;
            }
            CommonSensorValue commonValue = ToCommonSensorValue(dataObject);
            EnqueueData(commonValue);
        }

        public override CommonSensorValue GetLastValue()
        {
            try
            {
                DoubleBarSensorValue dataObject = GetPartialDataObject();
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
            DoubleBarSensorValue dataObject = GetDataObject();
            CommonSensorValue commonValue = ToCommonSensorValue(dataObject);
            EnqueueData(commonValue);
        }

        public void AddValue(double value)
        {
            lock (_syncObject)
            {
                _valuesList.Add(value);
            }
        }

        private CommonSensorValue ToCommonSensorValue(DoubleBarSensorValue data)
        {
            CommonSensorValue result = new CommonSensorValue();
            string serializedValue = GetStringData(data);
            result.TypedValue = serializedValue;
            result.SensorType = SensorType.DoubleBarSensor;
            return result;
        }
        private DoubleBarSensorValue GetPartialDataObject()
        {
            DoubleBarSensorValue result = new DoubleBarSensorValue();
            List<double> currentValues;
            lock (_syncObject)
            {
                currentValues = new List<double>(_valuesList);

                result.StartTime = barStart;
            }

            result.LastValue = currentValues.Last();
            currentValues.Sort();
            FillNumericData(result, currentValues);
            FillCommonData(result);
            result.EndTime = DateTime.MinValue.ToUniversalTime();

            return result;
        }
        private DoubleBarSensorValue GetDataObject()
        {
            DoubleBarSensorValue result = new DoubleBarSensorValue();
            List<double> collected;
            lock (_syncObject)
            {
                collected = new List<double>(_valuesList);
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

        private void FillNumericData(DoubleBarSensorValue value, List<double> values)
        {
            value.Max = GetRoundedNumber(values.Last());
            value.Min = GetRoundedNumber(values.First());
            value.Count = values.Count;
            value.Mean = GetRoundedNumber(CountMean(values));
            value.Percentiles.Add(new PercentileValueDouble(GetRoundedNumber(GetPercentile(values, 0.25)), 0.25));
            value.Percentiles.Add(new PercentileValueDouble(GetRoundedNumber(GetPercentile(values, 0.5)), 0.5));
            value.Percentiles.Add(new PercentileValueDouble(GetRoundedNumber(GetPercentile(values, 0.75)), 0.75));
        }

        private void FillCommonData(DoubleBarSensorValue value)
        {
            value.Key = ProductKey;
            value.Path = Path;
            value.Time = DateTime.Now;
        }
        protected override string GetStringData(SensorValueBase data)
        {
            try
            {
                DoubleBarSensorValue typedData = (DoubleBarSensorValue)data;
                return JsonConvert.SerializeObject(typedData);
                //return Serializer.Serialize(typedData);
            }
            catch (System.Exception e)
            {
                Console.WriteLine(e);
                return string.Empty;
            }
        }

        private double CountMean(List<double> values)
        {
            double sum = values.Sum();
            double mean = 0.0;
            try
            {
                mean = sum / values.Count;
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return mean;
        }

        private double GetRoundedNumber(double number)
        {
            return Math.Round(number, _precision, MidpointRounding.AwayFromZero);
        }
    }
}
