using System;
using System.Collections.Generic;
using HSMDataCollector.Extensions;
using HSMDataCollector.Options;
using HSMDataCollector.Sensors;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// #1102-C1: the file sensor buffers the whole file into memory, so the user-settable
    /// MaxFileSizeBytes must be bounded by a hard ceiling (a 1 GB configured cap meant ~6 GB peak per
    /// send: byte[] + List copy + JSON numeric-array serialization). The extra List copy is removed
    /// via a zero-copy wrap with a safe fallback.
    /// </summary>
    public sealed class FileSensorBoundsTests
    {
        [Fact]
        public void Effective_cap_passes_through_values_within_the_ceiling()
        {
            Assert.Equal(10L * 1024 * 1024, FileSensorInstant.GetEffectiveMaxFileSizeBytes(10L * 1024 * 1024));
        }

        [Fact]
        public void Effective_cap_bounds_oversized_configured_values()
        {
            Assert.Equal(FileSensorOptions.MaxAllowedFileSizeBytes, FileSensorInstant.GetEffectiveMaxFileSizeBytes(long.MaxValue));
            Assert.Equal(FileSensorOptions.MaxAllowedFileSizeBytes, FileSensorInstant.GetEffectiveMaxFileSizeBytes(FileSensorOptions.MaxAllowedFileSizeBytes + 1));
        }

        [Fact]
        public void Effective_cap_allows_exactly_the_ceiling()
        {
            Assert.Equal(FileSensorOptions.MaxAllowedFileSizeBytes, FileSensorInstant.GetEffectiveMaxFileSizeBytes(FileSensorOptions.MaxAllowedFileSizeBytes));
        }

        [Fact]
        public void Default_file_size_cap_is_within_the_ceiling()
        {
            Assert.True(new FileSensorOptions().MaxFileSizeBytes <= FileSensorOptions.MaxAllowedFileSizeBytes);
        }

        [Fact]
        public void AsList_preserves_content_and_count()
        {
            var bytes = new byte[] { 1, 2, 3, 255, 0, 42 };

            var list = bytes.AsList();

            Assert.Equal(bytes.Length, list.Count);
            Assert.Equal(bytes, list);
        }

        [Fact]
        public void AsList_of_empty_array_is_empty()
        {
            Assert.Empty(new byte[0].AsList());
        }

        [Fact]
        public void AsList_does_not_copy_the_buffer_when_zero_copy_is_supported()
        {
            var bytes = new byte[] { 1, 2, 3 };
            var list = bytes.AsList();

            if (!ByteCollectionExtensions.ZeroCopySupported)
                return; // Fallback path copies by design; content equality is covered above.

            bytes[1] = 99;

            Assert.Equal(new List<byte> { 1, 99, 3 }, list);
        }
    }
}
