using System.ComponentModel;
using System.Runtime.Serialization;

namespace HSMSensorDataObjects.HistoryRequests
{
    public sealed class FileHistoryRequest : HistoryRequest
    {
        [DataMember]
        [DefaultValue("temp")]
        public string FileName { get; set; }

        [DataMember]
        [DefaultValue("csv")]
        public string Extension { get; set; }

        [DataMember]
        public bool IsZipArchive { get; set; }
    }
}
