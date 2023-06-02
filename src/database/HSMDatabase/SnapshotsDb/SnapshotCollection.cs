using HSMDatabase.AccessManager;
using HSMDatabase.AccessManager.DatabaseEntities.SnapshotEntity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace HSMDatabase.SnapshotsDb
{
    internal sealed class SnapshotCollection<T> : IEntitySnapshotCollection<SensorStateEntity>
    {
        private static readonly JsonSerializerOptions _options = new()
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
            WriteIndented = true,
        };


        public Dictionary<Guid, SensorStateEntity> Data { get; set; }


        internal required string FilePath { get; init; }
        

        public Dictionary<Guid, SensorStateEntity> Read()
        {
            using var fs = new StreamReader(FilePath);

            Data = JsonSerializer.Deserialize<Dictionary<Guid, SensorStateEntity>>(fs.ReadToEnd());

            return Data;
        }

        public Task Save()
        {
            if (Data is null || Data.Count == 0)
                return Task.CompletedTask;

            using var fs = new StreamWriter(FilePath);

            return fs.WriteLineAsync(JsonSerializer.Serialize(Data, _options));
        }
    }
}
