using System;

namespace HSMClientWPFControls.Objects
{
    public class NumericDataPoint
    {
        public NumericDataPoint(DateTime date, double value)
        {
            Date = date;
            Value = value;
        }
        public DateTime Date { get; set; }
        public double Value { get; set; }
    }
}
