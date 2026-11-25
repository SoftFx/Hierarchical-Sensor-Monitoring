using ICSharpCode.SharpZipLib;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.IO;
using System.Threading;

namespace HSMServer.Core.LargeFiles.Archives
{
    /// <summary>
    /// <see cref="IArchiveWriter"/> over SharpZipLib: one entry, WinZip AES-256 (AE-2), Zip64 always on, Deflate.
    /// </summary>
    public sealed class SharpZipArchiveWriter : IArchiveWriter
    {
        private const int BufferSize = 1 << 20;

        // The range of a DOS date and time, the only time a zip entry keeps without an extra field.
        private static readonly DateTime _minDosTime = new(1980, 1, 1, 0, 0, 0);
        private static readonly DateTime _maxDosTime = new(2107, 12, 31, 23, 59, 58);


        public void Write(Stream output, string entryName, DateTime entryTimeUtc, long size, Stream content, string password, int deflateLevel, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(output);
            ArgumentNullException.ThrowIfNull(content);
            ArgumentException.ThrowIfNullOrEmpty(entryName);
            ArgumentException.ThrowIfNullOrEmpty(password);
            ArgumentOutOfRangeException.ThrowIfNegative(size);
            ArgumentOutOfRangeException.ThrowIfLessThan(deflateLevel, 0);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(deflateLevel, 9);

            // A forward-only output makes SharpZipLib write a data descriptor instead of patching the local header, so the
            // bytes written are the bytes of the archive (the builder hashes them as they pass). With such an output a
            // Stored entry becomes Deflate at level 0, which every reader opens the same way.
            var zip = new ZipOutputStream(output, BufferSize)
            {
                IsStreamOwner = false,
                UseZip64 = UseZip64.On,
                Password = password,
            };

            var finished = false;
            try
            {
                zip.SetLevel(deflateLevel);

                var entry = new ZipEntry(entryName)
                {
                    AESKeySize = 256,
                    CompressionMethod = deflateLevel == 0 ? CompressionMethod.Stored : CompressionMethod.Deflated,
                    // A zip entry keeps a local DOS time without a zone: the UTC value is written as it is, so an archiver
                    // shows the source's UTC time.
                    DateTime = ToDosRange(entryTimeUtc),
                    Size = size,
                };

                zip.PutNextEntry(entry);

                var buffer = new byte[BufferSize];
                int read;
                while ((read = content.Read(buffer, 0, buffer.Length)) > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    zip.Write(buffer, 0, read);
                }

                ct.ThrowIfCancellationRequested();
                zip.CloseEntry();
                zip.Finish();
                finished = true;
            }
            finally
            {
                try
                {
                    zip.Dispose();
                }
                catch when (!finished)
                {
                    // Disposing an unfinished archive finishes it and may throw again: the first exception is the one to report.
                }
            }
        }

        public ArchiveEntryReader OpenEntry(Stream archive, string password)
        {
            ArgumentNullException.ThrowIfNull(archive);
            ArgumentException.ThrowIfNullOrEmpty(password);

            ZipFile zip = null;
            try
            {
                zip = new ZipFile(archive, leaveOpen: false) { Password = password };

                if (zip.Count != 1)
                    throw new ArchiveFormatException($"The archive holds {zip.Count} entries instead of one.");

                var entry = zip[0];
                if (!entry.IsFile)
                    throw new ArchiveFormatException("The only entry of the archive is not a file.");
                if (entry.AESKeySize != 256)
                    throw new ArchiveFormatException("The entry of the archive is not encrypted with AES-256.");

                var content = new GuardedEntryStream(zip.GetInputStream(entry));
                var time = DateTime.SpecifyKind(entry.DateTime, DateTimeKind.Utc); // written as UTC, see Write

                return new ArchiveEntryReader(entry.Name, entry.Size, time, content, zip);
            }
            catch (Exception ex)
            {
                zip?.Close();

                if (ex is ArchiveFormatException)
                    throw;

                throw Map(ex) ?? new ArchiveFormatException($"The archive cannot be opened ({ex.GetType().Name}).", ex);
            }
        }


        private static DateTime ToDosRange(DateTime time)
        {
            var value = DateTime.SpecifyKind(time, DateTimeKind.Unspecified);

            return value < _minDosTime ? _minDosTime : value > _maxDosTime ? _maxDosTime : value;
        }

        // SharpZipLib reports a wrong password, a failed authentication code and a damaged archive with its own exceptions;
        // none of their messages carries the password, and the texts here never do either.
        private static ArchiveFormatException Map(Exception ex) => ex switch
        {
            ZipException => new ArchiveFormatException("The archive cannot be read: a wrong password, or the archive is damaged.", ex),
            SharpZipBaseException => new ArchiveFormatException("The archive is damaged.", ex),
            InvalidDataException => new ArchiveFormatException("The archive is damaged.", ex),
            EndOfStreamException => new ArchiveFormatException("The archive ends too early.", ex),
            _ => null,
        };


        /// <summary>The decompressed content: the library's exceptions while reading become <see cref="ArchiveFormatException"/>.</summary>
        private sealed class GuardedEntryStream(Stream inner) : Stream
        {
            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }


            public override int Read(byte[] buffer, int offset, int count)
            {
                try
                {
                    return inner.Read(buffer, offset, count);
                }
                catch (Exception ex) when (Map(ex) is { } mapped)
                {
                    throw mapped;
                }
            }

            public override void Flush() { }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                    inner.Dispose();

                base.Dispose(disposing);
            }
        }
    }
}
