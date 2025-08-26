using HSMSensorDataObjects.HistoryRequests;
using System;


namespace HSMServer.Core.Model.Requests
{
    public record HistoryRequestModel : BaseUpdateRequest
    {
        public DateTime From { get; set; }

        public DateTime? To { get; set; }

        public int? Count { get; set; }

        public RequestOptions Options { get; set; }

        public Guid Key { get; set; }

        public HistoryRequestModel(Guid key, string path) : base (string.Empty, path)
        {
            Key = key;
        }

    }


    public record SensorHistoryRequest()
    {
        public DateTime From { get; init; }

        public DateTime To { get; init; }

        public int Count { get; init; }

        public RequestOptions Options { get; init; }
    }
}