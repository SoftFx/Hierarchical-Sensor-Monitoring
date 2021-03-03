namespace HSMSensorDataObjects.BarData
{
    public class PercentileValueInt
    {
        public PercentileValueInt()
        {
            Value = 0;
            Percentile = 0;
        }
        public PercentileValueInt(int value, double percentile)
        {
            Value = value;
            Percentile = percentile;
        }

        public int Value { get; set; }

        public double Percentile { get; set; }
    }
}