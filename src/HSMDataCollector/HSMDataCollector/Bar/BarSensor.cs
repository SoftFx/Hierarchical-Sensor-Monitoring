using HSMDataCollector.Core;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;
using System.Linq;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.Bar
{
    internal class BarSensor<T> : BarSensorBase, IBarSensor<T> where T : struct
    {
        private readonly SensorType _type;
        private readonly List<T> _valuesList;
        private int _precision;

        public BarSensor(string path, string productKey, IValuesQueue queue, int barTimerPeriod, int smallTimerPeriod,
            SensorType type, string description = "")
            : base(path, productKey, queue, barTimerPeriod, smallTimerPeriod, description)
        {
            _valuesList = new List<T>();
            _type = type;
        }
        public BarSensor(string path, string productKey, IValuesQueue queue, int barTimerPeriod, int smallTimerPeriod, SensorType type, int precision = 2,
            string description = "") : this(path, productKey, queue, barTimerPeriod, smallTimerPeriod, type, description)
        {
            FillPrecision(precision);
        }

        public void AddValue(T value)
        {
            lock (_syncObject)
            {
                _valuesList.Add(value);
            }
        }
        private void FillPrecision(int precision)
        {
            if (precision < 1 || precision > 10)
            {
                _precision = 2;
                return;
            }

            _precision = precision;
        }
        protected override void SendDataTimer(object state)
        {
            List<T> collected;
            DateTime endTime;
            DateTime startTime;
            lock (_syncObject)
            {
                collected = new List<T>(_valuesList);
                startTime = barStart;
                endTime = DateTime.Now;
                barStart = DateTime.Now;
            }

            SensorValueBase dataObject = GetSensorValueFromGenericList(collected, startTime, endTime);
            EnqueueValue(dataObject);
        }

        protected override void SmallTimerTick(object state)
        {
            List<T> collected;
            DateTime startTime;
            lock (_syncObject)
            {
                collected = new List<T>(_valuesList);
                startTime = barStart;
            }

            SensorValueBase dataObject = GetSensorValueFromGenericList(collected, startTime);
            EnqueueValue(dataObject);
        }

        public override CommonSensorValue GetLastValue()
        {
            throw new NotImplementedException();
        }

        protected override string GetStringData(SensorValueBase data)
        {
            throw new NotImplementedException();
        }

        public override SensorValueBase GetLastValueNew()
        {
            try
            {
                List<T> collected;
                DateTime startTime;
                lock (_syncObject)
                {
                    collected = new List<T>(_valuesList);
                    startTime = barStart;
                }

                return GetSensorValueFromGenericList(collected, startTime);
            }
            catch (Exception e)
            {
                return null;
            }
        }


        private SensorValueBase GetSensorValueFromGenericList(List<T> values, DateTime barStart, DateTime? barEnd = null)
        {
            try
            {
                if (_type == SensorType.DoubleBarSensor)
                {
                    var doublesList = values.OfType<double>().ToList();
                    return GetDoubleDataObject(doublesList, barStart, barEnd ?? DateTime.MinValue);
                }

                var intList = values.OfType<int>().ToList();
                return GetIntegerDataObject(intList, barStart, barEnd ?? DateTime.MinValue);
            }
            catch (Exception e)
            {
                return null;
            }
            
        }

        private void FillCommonData(SensorValueBase valueBase, DateTime time)
        {
            valueBase.Key = ProductKey;
            valueBase.Path = Path;
            valueBase.Type = _type;
            valueBase.Time = time;
            valueBase.Description = Description;
        }
        #region Double methods

        private DoubleBarSensorValue GetDoubleDataObject(List<double> values, DateTime barStartTime, DateTime barEndTime)
        {
            DoubleBarSensorValue result = new DoubleBarSensorValue();
            result.LastValue = values.Any() ? values.Last() : 0;
            FillCommonData(result, barStartTime);
            FillNumericData(result, values);
            result.StartTime = barStartTime;
            result.EndTime = barEndTime;
            return result;
        }

        private void FillNumericData(DoubleBarSensorValue value, List<double> values)
        {
            if (values.Any())
            {
                values.Sort();
                value.Max = GetRoundedNumber(values.Last());
                value.Min = GetRoundedNumber(values.First());
                value.Count = values.Count;
                value.Mean = GetRoundedNumber(CountMean(values));
                value.Percentiles.Add(new PercentileValueDouble(GetRoundedNumber(GetPercentile(values, 0.25)), 0.25));
                value.Percentiles.Add(new PercentileValueDouble(GetRoundedNumber(GetPercentile(values, 0.5)), 0.5));
                value.Percentiles.Add(new PercentileValueDouble(GetRoundedNumber(GetPercentile(values, 0.75)), 0.75));
                return;
            }

            value.Max = 0.0;
            value.Min = 0.0;
            value.Count = 0;
            value.Mean = 0.0;
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

        #endregion

        #region Int methods

        private IntBarSensorValue GetIntegerDataObject(List<int> values, DateTime barStartTime, DateTime barEndTime)
        {
            IntBarSensorValue result = new IntBarSensorValue();
            result.LastValue = values.Any() ? values.Last() : 0;
            FillCommonData(result, barStartTime);
            FillNumericData(result, values);
            result.StartTime = barStartTime;
            result.EndTime = barEndTime;
            return result;
        }

        private void FillNumericData(IntBarSensorValue value, List<int> values)
        {
            if (values.Any())
            {
                values.Sort();
                value.Max = values.Last();
                value.Min = values.First();
                value.Count = values.Count;
                value.Mean = CountMean(values);
                value.Percentiles.Add(new PercentileValueInt(GetPercentile(values, 0.25), 0.25));
                value.Percentiles.Add(new PercentileValueInt(GetPercentile(values, 0.5), 0.5));
                value.Percentiles.Add(new PercentileValueInt(GetPercentile(values, 0.75), 0.75));
                return;
            }

            value.Max = 0;
            value.Min = 0;
            value.Count = 0;
            value.Mean = 0;
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
        #endregion
    }
}
