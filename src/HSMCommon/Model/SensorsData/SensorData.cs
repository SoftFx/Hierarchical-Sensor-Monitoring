using System;
using HSMSensorDataObjects;

namespace HSMCommon.Model.SensorsData
{
    public class SensorData
    {
        public string Path { get; set; }
        public string Product { get; set; }
        public string Key { get; set; }
        public SensorType SensorType { get; set; }
        public DateTime Time { get; set; }
        public string StringValue { get; set; }
        public SensorStatus Status { get; set; }
        //public TransactionType TransactionType { get; set; }
        public string Description { get; set; }
        public string ShortStringValue { get; set; }
        public SensorData Clone()
        {
            SensorData copy = new SensorData();
            copy.Path = Path;
            copy.Product = Product;
            copy.Key = Key;
            copy.SensorType = SensorType;
            copy.Time = Time;
            copy.StringValue = StringValue;
            copy.Status = Status;
            copy.TransactionType = TransactionType;
            copy.Description = Description;
            copy.ShortStringValue = ShortStringValue;
            return copy;
        }
    }
}
