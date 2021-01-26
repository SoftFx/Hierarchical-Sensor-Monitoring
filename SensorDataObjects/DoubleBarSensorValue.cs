namespace HSMSensorDataObjects
{
    public class DoubleBarSensorValue : SensorValueBase, ISensorValue
    {
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public int Count { get; set; }
    }
}
