using System;

namespace HSMServer.Model
{
    public class NewJobResult
    {
        public bool Success { get; set; }
        public string Comment { get; set; }
        public DateTime Time { get; set; }
        public string ProductName { get; set; }
        public string SensorName { get; set; }
        public string Path { get; set; }
    }
}
