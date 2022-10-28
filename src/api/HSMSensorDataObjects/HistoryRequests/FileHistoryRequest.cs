using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.HistoryRequests
{
    public sealed class FileHistoryRequest : HistoryRequest
    {
        [DataMember]
        [DefaultValue("csv")]
        public string Format { get; set; }

        [DataMember]
        public bool IsZipArchive { get; set; }
    }
}
