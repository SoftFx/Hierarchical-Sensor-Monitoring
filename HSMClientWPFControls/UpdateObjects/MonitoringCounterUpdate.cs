namespace HSMClientWPFControls.UpdateObjects
{
    public class MonitoringCounterUpdate
    {
        public string Name { get; set; }
        public string ShortValue { get; set; }
        public object DataObject { get; set; }
        public CounterTypes CounterType { get; set; }
    }
}
