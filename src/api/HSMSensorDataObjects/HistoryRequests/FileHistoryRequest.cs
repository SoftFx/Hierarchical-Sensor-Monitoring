using System.ComponentModel;

namespace HSMSensorDataObjects.HistoryRequests
{
    public sealed class FileHistoryRequest : HistoryRequest
    {
        [DefaultValue("temp")]
        public string FileName { get; set; }

        [DefaultValue("csv")]
        public string Extension { get; set; }

        public bool IsZipArchive { get; set; }
    }
}
