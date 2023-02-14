﻿using HSMServer.Controllers;
using HSMServer.Extensions;
using System;
using System.Text.Json.Serialization;

namespace HSMServer.Model
{
    public class GetSensorHistoryModel
    {
        public string EncodedId { get; set; }

        public DateTime To { get; set; } = DateTime.MaxValue;

        public DateTime From { get; set; } = DateTime.MinValue;

        public int Type { get; set; }

        public int BarsCount { get; set; }


        [JsonIgnore]
        internal int Count { get; set; } = HomeController.MaxHistoryCount;

        [JsonIgnore]
        internal DateTime ToUtc => To.ToUtc();

        [JsonIgnore]
        internal DateTime FromUtc => From.ToUtc();
    }
}
