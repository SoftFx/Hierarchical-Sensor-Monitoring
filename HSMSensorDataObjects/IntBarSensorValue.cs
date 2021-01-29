namespace HSMSensorDataObjects
{
    public class IntBarSensorValue : SensorValueBase, ISensorValue
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public int Mean { get; set; }
        public int Count { get; set; }
    }
}
