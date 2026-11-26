using System.Text.Json.Serialization;

namespace HSMServer.Core.Model.HistoryValues
{
    public sealed class FileSensorHistory : SensorHistory
    {
        /// <summary>The content of an ordinary file value; empty for a large file value, whose content is never part of it.</summary>
        public byte[] Value { get; init; }

        public string FileName { get; init; }

        public string Extension { get; init; }

        /// <summary>The description of a large file value (#1885); absent for an ordinary file value.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public LargeFileHistory LargeFile { get; init; }
    }
}
