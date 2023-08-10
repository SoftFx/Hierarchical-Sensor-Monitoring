using System;

namespace HSMSensorDataObjects.HistoryRequests
{
    public class HistoryRequest : BaseRequest
    {
        public DateTime From { get; set; }

        public DateTime? To { get; set; }

        public int? Count { get; set; }
        
        public RequestOptions Options { get; set; }
    }

    [Flags]
    public enum RequestOptions
    {
        None = 0,
        IncludeTtlHistory,
    }
}
