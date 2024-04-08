﻿using HSMServer.Controllers;
using HSMServer.Extensions;
using System;
using System.Text.Json.Serialization;
using HSMSensorDataObjects.HistoryRequests;

namespace HSMServer.Model.Model.History
{
    public sealed record GetSensorHistoryRequest
    {
        [JsonIgnore]
        internal int Count { get; set; } = SensorHistoryController.MaxHistoryCount;

        [JsonIgnore]
        internal DateTime ToUtc => To.ToUtcKind();

        [JsonIgnore]
        internal DateTime FromUtc => From.ToUtcKind();


        public string EncodedId { get; set; }

        public int Type { get; set; }

        public int BarsCount { get; set; }

        public DateTime To { get; set; } = DateTime.MaxValue;

        public DateTime From { get; set; } = DateTime.MinValue;

        [JsonConverter(typeof(JsonStringEnumConverter))]
        public RequestOptions Options { get; set; }
    }
}
