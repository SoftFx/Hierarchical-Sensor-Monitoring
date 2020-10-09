using System;

namespace HSMServer.Model
{
    public class JobResult
    {
        public bool Success { get; set; }
        public string Comment { get; set; }
        public DateTime Time { get; set; }
        public string Path { get; set; }
        public string Key { get; set; }
    }
}
