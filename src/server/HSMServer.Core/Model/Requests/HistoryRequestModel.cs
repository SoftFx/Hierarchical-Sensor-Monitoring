using System;
using HSMSensorDataObjects.HistoryRequests;

namespace HSMServer.Core.Model.Requests
{
    public class HistoryRequestModel : BaseRequestModel
    {
        public DateTime From { get; set; }

        public DateTime? To { get; set; }

        public int? Count { get; set; }

        public RequestOptions Options { get; set; }

        public HistoryRequestModel(string key, string path) : base(key, path) { }
    }
}
