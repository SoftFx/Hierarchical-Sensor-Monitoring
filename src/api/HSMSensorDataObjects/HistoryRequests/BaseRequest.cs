using System.Runtime.Serialization;

namespace HSMSensorDataObjects.HistoryRequests
{
    public abstract class BaseRequest
    {
        [DataMember]
        public string Key { get; set; }

        [DataMember]
        public string Path { get; set; }
    }
}
