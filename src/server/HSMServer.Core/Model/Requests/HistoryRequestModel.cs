using HSMSensorDataObjects.HistoryRequests;
using System;

namespace HSMServer.Core.Model.Requests
{
    public class HistoryRequestModel : BaseRequestModel
    {
        public DateTime From { get; set; }

        public DateTime? To { get; set; }

        public int? Count { get; set; }

        public RequestOptions Options { get; set; }


        public HistoryRequestModel(string key, string path) : base(key, path) { }
        
        public HistoryRequestModel(Guid key, string path) : base(key, path) { }
    }


    public record SensorHistoryRequest()
    {
        public DateTime From { get; init; }

        public DateTime To { get; init; }

        public int Count { get; init; }

        public RequestOptions Options { get; init; }
    }
}