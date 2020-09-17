using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMgRPC.DataLayer.Model
{
    public class JobSensorData
    {
        public bool Success { get; set; }
        public string Comment { get; set; }
        public DateTime Time { get; set; }
        public long Timestamp { get; set; }
        public DateTime TimeCollected { get; set; }
    }
}
