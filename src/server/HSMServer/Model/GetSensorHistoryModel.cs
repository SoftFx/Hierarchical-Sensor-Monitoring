using System;

namespace HSMServer.Model
{
    public class GetSensorHistoryModel
    {
        public string EncodedId { get; set; }

        public DateTime To { get; set; } = DateTime.MaxValue;

        public DateTime From { get; set; } = DateTime.MinValue;

        public int Type { get; set; }
    }
}
