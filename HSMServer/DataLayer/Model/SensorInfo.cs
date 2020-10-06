using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.DataLayer.Model
{
    public class SensorInfo
    {
        public string Key { get; set; }
        public string Path { get; set; }
        public string ServerName { get; set; }
        public string SensorName { get; set; }
    }
}
