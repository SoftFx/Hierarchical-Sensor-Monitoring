using HSMDatabase.AccessManager;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HSMDatabase.SnapshotsDb
{
    internal sealed class SnapshotCollection<T> : IEntitySnapshotCollection<T>
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        };


        public Dictionary<Guid, T> Data { get; set; }


        internal required string FilePath { get; init; }


        public Dictionary<Guid, T> Read()
        {
            Data = File.Exists(FilePath)
                   ? JsonSerializer.Deserialize<Dictionary<Guid, T>>(File.ReadAllBytes(FilePath))
                   : new();

            return Data;
        }

        public Task Save() => Data is null || Data.Count == 0 ? Task.CompletedTask :
            File.WriteAllBytesAsync(FilePath, JsonSerializer.SerializeToUtf8Bytes(Data, _options));
    }
}
