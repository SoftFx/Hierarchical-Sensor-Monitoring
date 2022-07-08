using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class SensorEntity
    {
        [Obsolete("Remove this property after sensor entities migration")]
        [NonSerialized]
        public long ExpectedUpdateIntervalTicks;


        public string Id { get; init; }

        public string ProductId { get; init; }

        public string AuthorId { get; init; }

        public string DisplayName { get; init; }

        public string Description { get; init; }

        public string Unit { get; init; }

        public long CreationDate { get; init; }

        public byte Type { get; init; }

        public byte State { get; init; }

        public List<string> Policies { get; init; }


        [JsonIgnore]
        public bool IsConverted { get; set; }
    }
}