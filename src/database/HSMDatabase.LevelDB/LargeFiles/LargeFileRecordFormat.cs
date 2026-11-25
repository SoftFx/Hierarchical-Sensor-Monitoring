using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using HSMDatabase.AccessManager.LargeFiles;

namespace HSMDatabase.LevelDB.LargeFiles
{
    /// <summary>
    /// The text of a <c>.rec</c> file (#1883): one JSON object, version 1. Members added later (the client name, the remote
    /// address) are optional: a record without them reads with null. Ids in the <c>N</c> format, times as UTC
    /// round-trip strings, hashes as lowercase hex, the state by its name. Unknown members are ignored, so a later version
    /// may add members; a missing or other version is refused.
    /// </summary>
    internal static class LargeFileRecordFormat
    {
        public const int Version = 1;

        private static readonly JsonSerializerOptions _options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };


        public static string Write(LargeFileRecord record) => JsonSerializer.Serialize(new Dto
        {
            Version = Version,
            FileId = record.FileId.ToString("N"),
            ProductId = record.ProductId.ToString("N"),
            SensorId = record.SensorId.ToString("N"),
            Name = record.Name,
            Extension = record.Extension,
            Size = record.Size,
            SourceModifiedUtc = Time(record.SourceModifiedUtc),
            SourceIdentity = record.SourceIdentity,
            ClientName = record.ClientName,
            RemoteAddress = record.RemoteAddress,
            PieceSize = record.PieceSize,
            PieceCount = record.PieceCount,
            PartLength = record.PartLength,
            Hash = Hex(record.Hash),
            State = record.State.ToString(),
            CreatedUtc = Time(record.CreatedUtc),
            LastPieceUtc = Time(record.LastPieceUtc),
            ValueTimeUtc = record.ValueTimeUtc is { } value ? Time(value) : null,
            ArchiveId = record.ArchiveId?.ToString("N"),
            PreviousArchiveIds = record.PreviousArchiveIds is { Count: > 0 } previous ? [.. previous.Select(id => id.ToString("N"))] : null,
            PasswordVersion = record.PasswordVersion,
            ArchiveLength = record.ArchiveLength,
            ArchiveHash = Hex(record.ArchiveHash),
        }, _options);

        /// <summary>The record of a <c>.rec</c> text; <see cref="FormatException"/> when it is not one.</summary>
        public static LargeFileRecord Read(string text)
        {
            Dto dto;

            try
            {
                dto = JsonSerializer.Deserialize<Dto>(text, _options);
            }
            catch (JsonException ex)
            {
                throw new FormatException($"not a record: {ex.Message}", ex);
            }

            if (dto is null || dto.Version != Version)
                throw new FormatException($"the record version is {dto?.Version.ToString() ?? "missing"}, expected {Version}");

            if (!Enum.TryParse<LargeFileState>(dto.State, out var state) || !Enum.IsDefined(state))
                throw new FormatException($"unknown state '{dto.State}'");

            return new LargeFileRecord
            {
                FileId = Id(dto.FileId, "fileId"),
                ProductId = Id(dto.ProductId, "productId"),
                SensorId = Id(dto.SensorId, "sensorId"),
                Name = dto.Name ?? string.Empty,
                Extension = dto.Extension,
                Size = dto.Size,
                SourceModifiedUtc = ParseTime(dto.SourceModifiedUtc, "sourceModifiedUtc"),
                SourceIdentity = dto.SourceIdentity,
                ClientName = dto.ClientName,
                RemoteAddress = dto.RemoteAddress,
                PieceSize = dto.PieceSize > 0 ? dto.PieceSize : throw new FormatException($"pieceSize {dto.PieceSize}"),
                PieceCount = dto.PieceCount >= 0 ? dto.PieceCount : throw new FormatException($"pieceCount {dto.PieceCount}"),
                PartLength = dto.PartLength >= 0 ? dto.PartLength : throw new FormatException($"partLength {dto.PartLength}"),
                Hash = Bytes(dto.Hash, "hash"),
                State = state,
                CreatedUtc = ParseTime(dto.CreatedUtc, "createdUtc"),
                LastPieceUtc = ParseTime(dto.LastPieceUtc, "lastPieceUtc"),
                ValueTimeUtc = dto.ValueTimeUtc is null ? null : ParseTime(dto.ValueTimeUtc, "valueTimeUtc"),
                ArchiveId = dto.ArchiveId is null ? null : Id(dto.ArchiveId, "archiveId"),
                PreviousArchiveIds = dto.PreviousArchiveIds is null ? [] : [.. dto.PreviousArchiveIds.Select(id => Id(id, "previousArchiveIds"))],
                PasswordVersion = dto.PasswordVersion,
                ArchiveLength = dto.ArchiveLength,
                ArchiveHash = Bytes(dto.ArchiveHash, "archiveHash"),
            };
        }


        private static string Time(DateTime time) => time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

        private static DateTime ParseTime(string text, string name) =>
            DateTime.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var time)
                ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
                : throw new FormatException($"{name} '{text}' is not a time");

        private static Guid Id(string text, string name) =>
            Guid.TryParseExact(text, "N", out var id) ? id : throw new FormatException($"{name} '{text}' is not an id");

        private static string Hex(byte[] bytes) => bytes is null ? null : Convert.ToHexString(bytes).ToLowerInvariant();

        private static byte[] Bytes(string hex, string name)
        {
            if (hex is null)
                return null;

            try
            {
                return Convert.FromHexString(hex);
            }
            catch (FormatException ex)
            {
                throw new FormatException($"{name} is not hex", ex);
            }
        }


        private sealed class Dto
        {
            public int Version { get; set; }
            public string FileId { get; set; }
            public string ProductId { get; set; }
            public string SensorId { get; set; }
            public string Name { get; set; }
            public string Extension { get; set; }
            public long Size { get; set; }
            public string SourceModifiedUtc { get; set; }
            public string SourceIdentity { get; set; }
            public string ClientName { get; set; }
            public string RemoteAddress { get; set; }
            public int PieceSize { get; set; }
            public long PieceCount { get; set; }
            public long PartLength { get; set; }
            public string Hash { get; set; }
            public string State { get; set; }
            public string CreatedUtc { get; set; }
            public string LastPieceUtc { get; set; }
            public string ValueTimeUtc { get; set; }
            public string ArchiveId { get; set; }
            public List<string> PreviousArchiveIds { get; set; }
            public int? PasswordVersion { get; set; }
            public long? ArchiveLength { get; set; }
            public string ArchiveHash { get; set; }
        }
    }
}
