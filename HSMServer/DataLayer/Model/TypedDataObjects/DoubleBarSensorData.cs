namespace HSMServer.DataLayer.Model.TypedDataObjects
{
    public class DoubleBarSensorData
    {
        public string Comment { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public int Count { get; set; }
    }
}
