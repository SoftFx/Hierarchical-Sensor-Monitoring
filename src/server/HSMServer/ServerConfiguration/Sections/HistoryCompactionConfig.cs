using System;
using System.Text.Json.Serialization;
using HSMServer.Core.DataLayer;

namespace HSMServer.ServerConfiguration
{
    // The background compaction of closed weekly shards of sensor history and journal (#1801). Read once at startup,
    // like HistorySealing: a change takes effect on the next server start. The Compact button on the Configuration page
    // and the compaction a seal does of its own chunk database do not depend on it.
    public sealed class HistoryCompactionConfig
    {
        // Off by default: an upgrade changes nothing on disk until the operator switches it on. The first run after the
        // switch finds every closed week due, and a large deployment then rewrites its disk for many hours.
        public const bool DefaultEnabled = false;


        public bool Enabled { get; set; } = DefaultEnabled;

        // The daily UTC window the background compaction works in, "HH:mm". Equal values (the default) mean all day; a
        // window may wrap past midnight ("22:00" to "06:00"). A compaction running when the window ends is not stopped.
        public string WorkingHoursStartUtc { get; set; } = "00:00";

        public string WorkingHoursEndUtc { get; set; } = "00:00";


        public void Validate() => _ = WorkingHours;

        [JsonIgnore]
        public DailyUtcWindow WorkingHours => new(ParseTime(WorkingHoursStartUtc, nameof(WorkingHoursStartUtc)),
                                                  ParseTime(WorkingHoursEndUtc, nameof(WorkingHoursEndUtc)));


        private static TimeSpan ParseTime(string value, string name) => UtcTimeOfDay.Parse(value, nameof(IServerConfig.HistoryCompaction), name);
    }
}
