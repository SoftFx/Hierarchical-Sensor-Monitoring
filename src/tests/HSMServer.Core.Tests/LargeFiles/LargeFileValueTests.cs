using System;
using HSMCommon.Model;
using HSMDatabase.AccessManager.Formatters;
using HSMServer.Core.Extensions;
using HSMServer.Core.Model;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles
{
    /// <summary>
    /// The large value of a File sensor (#1883): a derived record under its own MemoryPack union tag, so the rows of
    /// ordinary file values stay byte for byte what they were, and an old server fails only on the large rows.
    /// </summary>
    public class LargeFileValueTests
    {
        private static readonly DateTime Time = new(2026, 11, 25, 10, 0, 0, DateTimeKind.Utc);

        private readonly MemoryPackFormatter _formatter = new();


        // The bytes of an ordinary file value, taken on the commit before LargeFileValue existed. They must never change:
        // every file row already stored, and every row a server without the large value reads, has this layout.
        private const string OrdinaryFileValueHex =
            "080c001022deda4adf4800ffffffff001022deda4adf48000000000000000000000000000000000001000000000000000000000000000000000000000000000000000000fbffffff0400000064756d70fcffffff03000000646d700000000000000000";

        [Fact]
        public void An_ordinary_file_value_serializes_to_the_same_bytes_as_before_the_large_value()
        {
            var bytes = _formatter.Serialize(Ordinary());

            Assert.Equal(OrdinaryFileValueHex, Convert.ToHexString(bytes).ToLowerInvariant());
            Assert.IsType<FileValue>(_formatter.Deserialize(bytes));
        }

        [Fact]
        public void A_large_file_value_round_trips_under_its_own_union_tag()
        {
            var value = Large();
            var bytes = _formatter.Serialize(value);

            // MemoryPack writes the union tag first: 12 for a large value, 8 for an ordinary file value.
            Assert.Equal(LargeFileValue.UnionTag, bytes[0]);
            Assert.Equal(8, _formatter.Serialize(Ordinary())[0]);

            var back = Assert.IsType<LargeFileValue>(_formatter.Deserialize(bytes));

            Assert.Equal(value.FileId, back.FileId);
            Assert.Equal(value.Hash, back.Hash);
            Assert.Equal(value.SourceModified, back.SourceModified);
            Assert.Equal(value.Name, back.Name);
            Assert.Equal(value.Extension, back.Extension);
            Assert.Equal(value.OriginalSize, back.OriginalSize);
            Assert.Empty(back.Value);
            Assert.Equal(value.Time, back.Time);
            Assert.Equal(SensorType.File, back.Type);
            Assert.True(LargeFileValue.IsLargeFileRow(bytes));
            Assert.False(LargeFileValue.IsLargeFileRow(_formatter.Serialize(Ordinary())));
        }

        [Fact]
        public void With_keeps_the_large_value_type()
        {
            BaseValue value = Large();

            var changed = value with { Status = SensorStatus.Error, Comment = "c" };

            Assert.IsType<LargeFileValue>(changed);
            Assert.Equal(((LargeFileValue)value).FileId, ((LargeFileValue)changed).FileId);
        }

        [Fact]
        public void A_large_file_value_is_never_deflated_or_inflated()
        {
            var value = Large();

            Assert.Same(value, value.CompressContent());
            Assert.Same(value, value.DecompressContent());

            var storage = new FileValuesStorage();
            storage.AddValue(value);

            var cached = Assert.IsType<LargeFileValue>(storage.LastValue);
            Assert.Empty(cached.Value);
            Assert.Equal(value.OriginalSize, cached.OriginalSize);
            Assert.Same(value, storage.PrepareForPersist(value));
        }

        [Fact]
        public void An_ordinary_file_value_is_still_deflated_and_inflated()
        {
            var content = new byte[4096];
            var value = Ordinary() with { Value = content, OriginalSize = content.Length };

            var compressed = value.CompressContent();

            Assert.True(compressed.Value.Length < content.Length);
            Assert.Equal(content, compressed.DecompressContent().Value);
        }


        private static FileValue Ordinary() => new()
        {
            Time = Time,
            ReceivingTime = Time,
            Value = [],
            Name = "dump",
            Extension = "dmp",
            OriginalSize = 0,
            Status = SensorStatus.Ok,
            Comment = null,
        };

        private static LargeFileValue Large() => new()
        {
            Time = Time,
            ReceivingTime = Time,
            Value = [],
            Name = "app.dmp",
            Extension = "dmp",
            OriginalSize = 8L * 1024 * 1024 * 1024,
            FileId = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
            Hash = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32],
            SourceModified = Time.AddHours(-1),
        };
    }
}
