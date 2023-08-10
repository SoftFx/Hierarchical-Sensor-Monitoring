﻿using HSMServer.Core.Model;
using System.Globalization;

namespace HSMServer.Model.History
{
    internal sealed class DoubleBarHistoryProcessor : BarHistoryProcessor<double>
    {
        private readonly NumberFormatInfo _format;


        protected override double DefaultMax { get; } = double.MinValue;

        protected override double DefaultMin { get; } = double.MaxValue;


        public DoubleBarHistoryProcessor()
        {
            _format = new NumberFormatInfo { NumberDecimalSeparator = "." };
        }

        protected override DoubleBarValue GetBarValue(SummaryBarItem<double> summary) =>
          new()
          {
              Count = summary.Count,
              OpenTime = summary.OpenTime.ToUniversalTime(),
              CloseTime = summary.CloseTime.ToUniversalTime(),
              Min = summary.Min,
              Max = summary.Max,
              Mean = summary.Mean,
              Percentiles = summary.Percentiles,
              Time = summary.CloseTime.ToUniversalTime(),
              ReceivingTime = summary.CloseTime.ToUniversalTime(),
          };

        protected override double Average(double value1, double value2) =>
            (value1 + value2) / 2;

        protected override double Convert(double value) => value;

        protected override double GetComposition(double value1, int value2) =>
            value1 * value2;
    }
}
