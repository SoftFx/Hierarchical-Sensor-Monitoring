using System;
using System.IO;
using System.Threading;
using HSMServer.Core.LargeFiles.Archives;
using ICSharpCode.SharpZipLib.Zip;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Archives
{
    // #1885: the zip writer of large file archives: one entry, AES-256, Zip64, Deflate.
    public sealed class SharpZipArchiveWriterTests
    {
        private const string Password = "Correct-Horse-Battery-Staple-42";

        private static readonly DateTime _time = new(2026, 11, 25, 10, 30, 0, DateTimeKind.Utc);

        private readonly SharpZipArchiveWriter _writer = new();


        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(0, false)]
        [InlineData(9, false)]
        public void RoundTrip_KeepsTheBytesTheNameTheSizeAndTheTime(int level, bool seekableOutput)
        {
            var content = TestContent.Compressible(3 * 1024 * 1024 + 17);

            var archive = Write(content, "dump.dmp", level, seekableOutput);

            using var entry = _writer.OpenEntry(new MemoryStream(archive), Password);
            Assert.Equal("dump.dmp", entry.Name);
            Assert.Equal(content.Length, entry.Size);
            Assert.Equal(_time, entry.EntryTime);
            Assert.Equal(DateTimeKind.Utc, entry.EntryTime.Kind);
            Assert.Equal(content, ReadAll(entry.Content));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CyrillicEntryName_RoundTrips_AndIsMarkedAsUtf8(bool seekableOutput)
        {
            const string name = "дамп процесса.dmp";
            var content = TestContent.Compressible(10_000);

            var archive = Write(content, name, seekableOutput: seekableOutput);

            using (var entry = _writer.OpenEntry(new MemoryStream(archive), Password))
            {
                Assert.Equal(name, entry.Name);
                Assert.Equal(content, ReadAll(entry.Content));
            }

            // General purpose flag bit 11: the name is UTF-8, so every archiver reads it the same way, whatever its code page.
            using var zip = new ZipFile(new MemoryStream(archive), leaveOpen: false);
            Assert.True(zip[0].IsUnicodeText);
            Assert.Equal(name, zip[0].Name);
        }

        [Fact]
        public void EmptyContent_MakesAnArchiveOfAnEmptyEntry()
        {
            var archive = Write([], "empty.bin");

            using var entry = _writer.OpenEntry(new MemoryStream(archive), Password);
            Assert.Equal(0, entry.Size);
            Assert.Empty(ReadAll(entry.Content));
        }

        [Fact]
        public void WrongPassword_IsAnArchiveFormatException_WithoutEitherPasswordInTheText()
        {
            var archive = Write(TestContent.Compressible(100_000), "dump.dmp");
            const string wrong = "Wrong-Password-Guess-0000";

            var ex = Assert.Throws<ArchiveFormatException>(() =>
            {
                using var entry = _writer.OpenEntry(new MemoryStream(archive), wrong);
                ReadAll(entry.Content);
            });

            Assert.DoesNotContain(wrong, ex.ToString());
            Assert.DoesNotContain(Password, ex.ToString());
        }

        [Fact]
        public void TinyFile_CarriesTheZip64AndTheAesExtraFields_AndAes256()
        {
            var archive = Write([1, 2, 3], "tiny.bin");

            using var zip = new ZipFile(new MemoryStream(archive), leaveOpen: false);
            var entry = zip[0];
            var extra = new ZipExtraData(entry.ExtraData);

            Assert.True(entry.IsCrypted);
            Assert.Equal(256, entry.AESKeySize);
            Assert.True(extra.Find(0x0001), "Zip64 extended information field");
            Assert.True(extra.Find(0x9901), "WinZip AES field");
        }

        [Fact]
        public void DamagedEncryptedData_FailsToRead_AsAnArchiveFormatException()
        {
            var content = TestContent.Random(200_000, seed: 3);
            var archive = Write(content, "dump.dmp");
            archive[archive.Length / 2] ^= 0x5A;

            Assert.Throws<ArchiveFormatException>(() =>
            {
                using var entry = _writer.OpenEntry(new MemoryStream(archive), Password);
                ReadAll(entry.Content);
            });
        }

        [Fact]
        public void ArchiveOfTwoEntries_IsRefused()
        {
            var buffer = new MemoryStream();
            using (var zip = new ZipOutputStream(buffer) { IsStreamOwner = false, Password = Password })
            {
                foreach (var name in new[] { "a.bin", "b.bin" })
                {
                    zip.PutNextEntry(new ZipEntry(name) { AESKeySize = 256 });
                    zip.Write([1, 2, 3], 0, 3);
                    zip.CloseEntry();
                }
            }

            var ex = Assert.Throws<ArchiveFormatException>(() => _writer.OpenEntry(new MemoryStream(buffer.ToArray()), Password));
            Assert.Contains("2 entries", ex.Message);
        }

        [Fact]
        public void NotAZip_IsAnArchiveFormatException()
        {
            Assert.Throws<ArchiveFormatException>(() => _writer.OpenEntry(new MemoryStream(TestContent.Random(10_000, seed: 1)), Password));
        }

        [Theory]
        [InlineData(1, false)]
        [InlineData(1, true)]
        [InlineData(0, false)]
        public void WithoutAPassword_RoundTripsUnencrypted_AndOpensOnlyWithoutAPassword(int level, bool seekableOutput)
        {
            var content = TestContent.Compressible(1024 * 1024 + 5);

            var archive = Write(content, "trace.log", level, seekableOutput, password: null);

            using (var entry = _writer.OpenEntry(new MemoryStream(archive), null))
            {
                Assert.Equal("trace.log", entry.Name);
                Assert.Equal(content.Length, entry.Size);
                Assert.Equal(_time, entry.EntryTime);
                Assert.Equal(content, ReadAll(entry.Content));
            }

            // An unencrypted entry is never taken for a secure one.
            var ex = Assert.Throws<ArchiveFormatException>(() => _writer.OpenEntry(new MemoryStream(archive), Password));
            Assert.Contains("not encrypted with AES-256", ex.Message);
        }

        [Fact]
        public void AnEncryptedEntry_IsRefusedWithoutAPassword()
        {
            var archive = Write(TestContent.Compressible(10_000), "dump.dmp");

            var ex = Assert.Throws<ArchiveFormatException>(() => _writer.OpenEntry(new MemoryStream(archive), null));

            Assert.Contains("is encrypted", ex.Message);
        }

        [Fact]
        public void TinyFileWithoutAPassword_CarriesTheZip64Field_AndNoEncryption()
        {
            var archive = Write([1, 2, 3], "tiny.bin", password: null);

            using var zip = new ZipFile(new MemoryStream(archive), leaveOpen: false);
            var entry = zip[0];
            var extra = new ZipExtraData(entry.ExtraData);

            Assert.False(entry.IsCrypted);
            Assert.Equal(0, entry.AESKeySize);
            Assert.True(extra.Find(0x0001), "Zip64 extended information field");
            Assert.False(extra.Find(0x9901), "WinZip AES field");
            Assert.Equal(CompressionMethod.Deflated, entry.CompressionMethod);
        }

        [Fact]
        public void AnEmptyPassword_IsRefused_ItIsNeitherSecureNorNot()
        {
            Assert.Throws<ArgumentException>(() =>
                _writer.Write(new MemoryStream(), "a.bin", _time, 3, new MemoryStream([1, 2, 3]), string.Empty, 1, CancellationToken.None));
            Assert.Throws<ArgumentException>(() => _writer.OpenEntry(new MemoryStream(), string.Empty));
        }

        [Fact]
        public void Cancellation_StopsTheWrite()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Assert.ThrowsAny<OperationCanceledException>(() =>
                _writer.Write(new ForwardOnlyStream(new MemoryStream()), "a.bin", _time, 3 << 20, new MemoryStream(TestContent.Compressible(3 << 20)), Password, 1, cts.Token));
        }


        private byte[] Write(byte[] content, string name, int level = 1, bool seekableOutput = false, string password = Password)
        {
            var buffer = new MemoryStream();
            Stream output = seekableOutput ? buffer : new ForwardOnlyStream(buffer);

            _writer.Write(output, name, _time, content.Length, new MemoryStream(content), password, level, CancellationToken.None);

            return buffer.ToArray();
        }

        internal static byte[] ReadAll(Stream stream)
        {
            var copy = new MemoryStream();
            stream.CopyTo(copy);
            return copy.ToArray();
        }


        /// <summary>The output the builder gives the writer: forward only, so no header is patched afterwards.</summary>
        internal sealed class ForwardOnlyStream(Stream inner) : Stream
        {
            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

            public override void Flush() => inner.Flush();

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }


    internal static class TestContent
    {
        /// <summary>Bytes that compress like a dump: runs of zeros, repeated text and some noise.</summary>
        public static byte[] Compressible(int length, int seed = 7)
        {
            var random = new Random(seed);
            var bytes = new byte[length];
            var text = "heap object header pointer value 0x00007ff6 "u8.ToArray();

            for (var offset = 0; offset < length; offset += 4096)
            {
                var page = bytes.AsSpan(offset, Math.Min(4096, length - offset));
                switch (random.Next(3))
                {
                    case 0:
                        break; // zeros
                    case 1:
                        for (var i = 0; i < page.Length; i++)
                            page[i] = text[i % text.Length];
                        break;
                    default:
                        random.NextBytes(page);
                        break;
                }
            }

            return bytes;
        }

        public static byte[] Random(int length, int seed)
        {
            var bytes = new byte[length];
            new Random(seed).NextBytes(bytes);
            return bytes;
        }
    }
}
