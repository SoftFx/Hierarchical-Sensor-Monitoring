using System;

namespace HSMServer.Model
{
    public class GetSensorHistoryModel
    {
        public string EncodedId { get; set; }
        
        public DateTime To { get; set; }
        
        public DateTime From { get; set; }
        
        public int Type { get; set; }
    }
}
