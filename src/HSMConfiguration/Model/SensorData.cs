using System;

namespace HSMCore.Model
{
    //[Serializable]
    public class SensorData
    {
        public bool Success { get; set; }
        public string Comment { get; set; }
        public DateTime Time { get; set; }
        public string Key { get; set; }
    }
}
