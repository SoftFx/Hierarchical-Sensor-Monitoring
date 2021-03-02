namespace HSMSensorDataObjects.BarData
{
    public readonly struct PercentileValueInt
    {
        public PercentileValueInt(int value, double percentile)
        {
            Value = value;
            Percentile = percentile;
        }

        public int Value { get; }

        public double Percentile { get; }
    }
}