using System.Runtime.Serialization;

namespace HSMSensorDataObjects.HistoryRequests
{
    public sealed class FileHistoryRequest : HistoryRequest
    {
        [DataMember]
        public string Format { get; set; }

        [DataMember]
        public bool IsArchive { get; set; }
    }
}
