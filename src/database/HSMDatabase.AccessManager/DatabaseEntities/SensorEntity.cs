using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace HSMDatabase.AccessManager.DatabaseEntities
{
    public class SensorEntity
    {
        [Obsolete]
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

        [Obsolete]
        public string Path { get; set; }
        [Obsolete]
        public string ProductName { get; set; }

        [JsonIgnore]
        public bool IsConverted { get; set; }
    }
}