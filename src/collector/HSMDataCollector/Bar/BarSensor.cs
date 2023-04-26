﻿using HSMDataCollector.Core;
using HSMDataCollector.PublicInterface;
using HSMSensorDataObjects;
using HSMSensorDataObjects.SensorValueRequests;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMDataCollector.Bar
{
    internal sealed class BarSensor<T> : BarSensorBase, IBarSensor<T> where T : struct
    {
        private readonly SensorType _type;
        private readonly List<T> _valuesList = new List<T>();


        public BarSensor(string path, IValuesQueue queue, SensorType type, int barTimerPeriod, int smallTimerPeriod,
            string description = "", int precision = 2)
            : base(path, queue, barTimerPeriod, smallTimerPeriod, description, precision)
        {
            _type = type;
        }

        public BarSensor(string path, IValuesQueue queue, SensorType type, int barTimerPeriod = 300000,
            int smallTimerPeriod = 15000, int precision = 2, string description = "")
            : this(path, queue, type, barTimerPeriod, smallTimerPeriod, description, precision) { }


        public void AddValue(T value)
        {
            lock (_syncObject)
            {
                _valuesList.Add(value);
            }
        }

        public override SensorValueBase GetLastValue()
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
            catch
            {
                return null;
            }
        }

        protected override void SendDataTimer(object state)
        {
            List<T> collected;
            DateTime startTime;

            lock (_syncObject)
            {
                collected = new List<T>(_valuesList);
                startTime = barStart;
                barStart = DateTime.Now;
                _valuesList.Clear();
            }

            var dataObject = GetSensorValueFromGenericList(collected, startTime);
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

            var dataObject = GetSensorValueFromGenericList(collected, startTime);
            EnqueueValue(dataObject);
        }

        private BarSensorValueBase GetSensorValueFromGenericList(List<T> values, DateTime barStart)
        {
            try
            {
                var barEnd = barStart + TimeSpan.FromMilliseconds(_barTimerPeriod);

                if (_type == SensorType.DoubleBarSensor)
                {
                    var doublesList = values.OfType<double>().ToList();
                    return GetDoubleDataObject(doublesList, barStart, barEnd);
                }

                var intList = values.OfType<int>().ToList();
                return GetIntegerDataObject(intList, barStart, barEnd);
            }
            catch
            {
                return null;
            }
        }

        private void FillCommonData(SensorValueBase valueBase, DateTime time)
        {
            valueBase.Path = Path;
            valueBase.Time = time.ToUniversalTime();
            valueBase.Status = SensorStatus.Ok;
        }

        #region Double methods

        private DoubleBarSensorValue GetDoubleDataObject(List<double> values, DateTime barStartTime, DateTime barEndTime)
        {
            var result = new DoubleBarSensorValue();

            FillCommonData(result, barStartTime);
            FillNumericData(result, values);

            result.LastValue = GetRoundedNumber(values.LastOrDefault());
            result.OpenTime = barStartTime.ToUniversalTime();
            result.CloseTime = barEndTime.ToUniversalTime();

            return result;
        }

        private void FillNumericData(DoubleBarSensorValue data, List<double> values)
        {
            if (values.Count != 0)
            {
                values.Sort();
                data.Max = GetRoundedNumber(values.Last());
                data.Min = GetRoundedNumber(values.First());
                data.Count = values.Count;
                data.Mean = GetRoundedNumber(CountMean(values));
                data.Percentiles.Add(0.25, GetRoundedNumber(GetPercentile(values, 0.25)));
                data.Percentiles.Add(0.5, GetRoundedNumber(GetPercentile(values, 0.5)));
                data.Percentiles.Add(0.75, GetRoundedNumber(GetPercentile(values, 0.75)));

                return;
            }

            data.Max = 0.0;
            data.Min = 0.0;
            data.Count = 0;
            data.Mean = 0.0;
        }

        private static double CountMean(List<double> values)
        {
            double sum = values.Sum();
            double mean = 0.0;

            try
            {
                mean = sum / values.Count;
            }
            catch { }

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
            var result = new IntBarSensorValue();

            FillCommonData(result, barStartTime);
            FillNumericData(result, values);

            result.LastValue = values.LastOrDefault();
            result.OpenTime = barStartTime.ToUniversalTime();
            result.CloseTime = barEndTime.ToUniversalTime();

            return result;
        }

        private void FillNumericData(IntBarSensorValue data, List<int> values)
        {
            if (values.Count != 0)
            {
                values.Sort();
                data.Max = values.Last();
                data.Min = values.First();
                data.Count = values.Count;
                data.Mean = CountMean(values);
                data.Percentiles.Add(0.25, GetPercentile(values, 0.25));
                data.Percentiles.Add(0.5, GetPercentile(values, 0.5));
                data.Percentiles.Add(0.75, GetPercentile(values, 0.75));

                return;
            }

            data.Max = 0;
            data.Min = 0;
            data.Count = 0;
            data.Mean = 0;
        }

        private static int CountMean(List<int> values)
        {
            decimal sum = CountSum(values);
            int mean = 0;

            try
            {
                mean = (int)(sum / values.Count);
            }
            catch { }

            return mean;
        }

        private static decimal CountSum(List<int> values)
        {
            decimal result = decimal.Zero;

            foreach (var number in values)
                result += number;

            return result;
        }

        #endregion
    }
}
