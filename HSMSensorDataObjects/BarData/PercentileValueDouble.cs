namespace HSMSensorDataObjects.BarData
{
    public readonly struct PercentileValueDouble
    {
        public PercentileValueDouble(double value, double percentile)
        {
            Value = value;
            Percentile = percentile;
        }

        public double Value { get; }

        public double Percentile { get; }
    }
}