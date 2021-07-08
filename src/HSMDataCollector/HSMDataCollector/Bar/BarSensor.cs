using HSMDataCollector.Core;
using HSMSensorDataObjects;
using HSMSensorDataObjects.BarData;
using HSMSensorDataObjects.FullDataObject;
using System;
using System.Collections.Generic;
using System.Linq;
using HSMDataCollector.PublicInterface;
using Newtonsoft.Json;

namespace HSMDataCollector.Bar
{
    internal class BarSensor<T> : BarSensorBase, IBarSensor<T> where T : struct
    {
        private readonly SensorType _type;
        private readonly List<T> _valuesList;
        private int _precision;
        public BarSensor(string path, string productKey, IValuesQueue queue, SensorType type, int barTimerPeriod, int smallTimerPeriod,
            string description = "")
            : base(path, productKey, queue, barTimerPeriod, smallTimerPeriod, description)
        {
            _valuesList = new List<T>();
            _type = type;
        }
        public BarSensor(string path, string productKey, IValuesQueue queue, SensorType type, int barTimerPeriod = 300000,
            int smallTimerPeriod = 15000, int precision = 2, string description = "") 
            : this(path, productKey, queue, type, barTimerPeriod, smallTimerPeriod, description)
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

            UnitedSensorValue dataObject = GetSensorValueFromGenericList(collected, startTime, endTime);
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

            UnitedSensorValue dataObject = GetSensorValueFromGenericList(collected, startTime);
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

        public override UnitedSensorValue GetLastValueNew()
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


        private UnitedSensorValue GetSensorValueFromGenericList(List<T> values, DateTime barStart, DateTime? barEnd = null)
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

        private void FillCommonData(UnitedSensorValue valueBase, DateTime time)
        {
            valueBase.Key = ProductKey;
            valueBase.Path = Path;
            valueBase.Type = _type;
            valueBase.Time = time;
            valueBase.Description = Description;
        }
        #region Double methods

        private UnitedSensorValue GetDoubleDataObject(List<double> values, DateTime barStartTime, DateTime barEndTime)
        {
            UnitedSensorValue result = new UnitedSensorValue();
            FillCommonData(result, barStartTime);
            DoubleBarData barData = new DoubleBarData();
            barData.LastValue = values.Any() ? values.Last() : 0;
            FillNumericData(barData, values);
            barData.StartTime = barStartTime;
            barData.EndTime = barEndTime;
            result.Data = JsonConvert.SerializeObject(barData);
            return result;
        }

        private void FillNumericData(DoubleBarData data, List<double> values)
        {
            if (values.Any())
            {
                values.Sort();
                data.Max = GetRoundedNumber(values.Last());
                data.Min = GetRoundedNumber(values.First());
                data.Count = values.Count;
                data.Mean = GetRoundedNumber(CountMean(values));
                data.Percentiles.Add(new PercentileValueDouble(GetRoundedNumber(GetPercentile(values, 0.25)), 0.25));
                data.Percentiles.Add(new PercentileValueDouble(GetRoundedNumber(GetPercentile(values, 0.5)), 0.5));
                data.Percentiles.Add(new PercentileValueDouble(GetRoundedNumber(GetPercentile(values, 0.75)), 0.75));
                return;
            }

            data.Max = 0.0;
            data.Min = 0.0;
            data.Count = 0;
            data.Mean = 0.0;
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

        private UnitedSensorValue GetIntegerDataObject(List<int> values, DateTime barStartTime, DateTime barEndTime)
        {
            UnitedSensorValue result = new UnitedSensorValue();
            FillCommonData(result, barStartTime);
            IntBarData barData = new IntBarData();
            barData.LastValue = values.Any() ? values.Last() : 0;
            FillNumericData(barData, values);
            barData.StartTime = barStartTime;
            barData.EndTime = barEndTime;
            result.Data = JsonConvert.SerializeObject(barData);
            return result;
        }

        private void FillNumericData(IntBarData data, List<int> values)
        {
            if (values.Any())
            {
                values.Sort();
                data.Max = values.Last();
                data.Min = values.First();
                data.Count = values.Count;
                data.Mean = CountMean(values);
                data.Percentiles.Add(new PercentileValueInt(GetPercentile(values, 0.25), 0.25));
                data.Percentiles.Add(new PercentileValueInt(GetPercentile(values, 0.5), 0.5));
                data.Percentiles.Add(new PercentileValueInt(GetPercentile(values, 0.75), 0.75));
                return;
            }

            data.Max = 0;
            data.Min = 0;
            data.Count = 0;
            data.Mean = 0;
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
