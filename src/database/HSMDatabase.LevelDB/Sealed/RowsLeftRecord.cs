using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HSMDatabase.AccessManager;
using NLog;

namespace HSMDatabase.LevelDB.Sealed
{
    /// <summary>
    /// The sensors of a sealed week that the sealing job left as rows on purpose (#1804): the codec refused them, their
    /// chunks did not verify, or a row could not be read. Kept in the chunk directory as a human-readable file, one line
    /// per sensor: id, reason, row count and a fingerprint of the rows as they were. A later pass tries such a sensor
    /// again only when its rows no longer match the fingerprint, so the same refusal is not re-encoded on every pass.
    /// A missing or unreadable file only costs a retry.
    /// </summary>
    internal sealed class RowsLeftRecord
    {
        private static readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<Guid, Entry> _entries = [];


        public int Count => _entries.Count;


        public static RowsLeftRecord Read(string sealedPath)
        {
            var record = new RowsLeftRecord();
            var path = Path.Combine(sealedPath, SealedWeekStages.RowsLeftFileName);

            try
            {
                if (!File.Exists(path))
                    return record;

                foreach (var line in File.ReadLines(path))
                {
                    if (line.Length == 0 || line[0] == '#')
                        continue;

                    var parts = line.Split(' ', 4);

                    if (parts.Length >= 3 && Guid.TryParse(parts[0], out var id))
                        record._entries[id] = new Entry(parts[1], parts[2], parts.Length > 3 ? parts[3] : string.Empty);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _logger.Warn($"Could not read {path}: {e.Message}; the sensors it lists will be tried again");
            }

            return record;
        }

        public void Write(string sealedPath)
        {
            var text = new StringBuilder();

            text.AppendLine("# Sensors of this sealed week left as rows by the sealing job (#1804): id, reason, fingerprint of their rows, detail.");
            text.AppendLine("# A sensor is tried again only when its rows no longer match the fingerprint. Deleting this file retries them all.");

            foreach (var (id, entry) in _entries.OrderBy(e => e.Key))
                text.AppendLine($"{id} {entry.Reason} {entry.Fingerprint} {entry.Detail.ReplaceLineEndings(" ")}");

            DurableFile.WriteAtomic(Path.Combine(sealedPath, SealedWeekStages.RowsLeftFileName), text.ToString());
        }

        public void Set(Guid sensorId, string reason, string fingerprint, string detail) => _entries[sensorId] = new Entry(reason, fingerprint, detail ?? string.Empty);

        public void Remove(Guid sensorId) => _entries.Remove(sensorId);

        public bool Matches(Guid sensorId, string fingerprint) => _entries.TryGetValue(sensorId, out var entry) && entry.Fingerprint == fingerprint;

        public bool Contains(Guid sensorId) => _entries.ContainsKey(sensorId);


        private sealed record Entry(string Reason, string Fingerprint, string Detail);
    }


    /// <summary>The fingerprint of a sensor's rows: their count and a hash of every key and value, in key order.</summary>
    internal sealed class RowsFingerprint : IDisposable
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private long _rows;


        public void Add(byte[] key, byte[] value)
        {
            Span<byte> lengths = stackalloc byte[8];
            BitConverter.TryWriteBytes(lengths, key.Length);
            BitConverter.TryWriteBytes(lengths[4..], value.Length);

            _hash.AppendData(lengths);
            _hash.AppendData(key);
            _hash.AppendData(value);
            _rows++;
        }

        public string Finish() => $"rows={_rows}/sha256={Convert.ToHexString(_hash.GetHashAndReset())[..32]}";

        public void Dispose() => _hash.Dispose();
    }
}
