using Newtonsoft.Json;
using System;

namespace HSMServer.Controllers.GrafanaDatasources.JsonSource
{
    public class TimeRange
    {
        public string From { get; set; }

        public string To { get; set; }


        [JsonIgnore]
        public DateTime FromUtc => DateTime.Parse(From).ToUniversalTime();

        [JsonIgnore]
        public DateTime ToUtc => DateTime.Parse(To).ToUniversalTime();
    }
}