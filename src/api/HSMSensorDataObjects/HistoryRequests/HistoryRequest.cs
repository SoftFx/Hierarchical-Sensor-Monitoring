using System;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.HistoryRequests
{
    public class HistoryRequest : BaseRequest
    {
        [DataMember]
        public DateTime From { get; set; } // TODO: From is nullable?? 

        [DataMember]
        public DateTime? To { get; set; }

        [DataMember]
        public int? Count { get; set; }
    }
}
