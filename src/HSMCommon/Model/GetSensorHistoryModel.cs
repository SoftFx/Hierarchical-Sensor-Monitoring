using System;

namespace HSMCommon.Model
{
    public class GetSensorHistoryModel
    {
        public string Path { get; set; }
        public DateTime To { get; set; }
        public DateTime From { get; set; }
        public int Type { get; set; }
    }
}
