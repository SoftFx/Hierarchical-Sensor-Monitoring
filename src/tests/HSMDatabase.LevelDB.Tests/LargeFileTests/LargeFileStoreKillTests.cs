using System.Diagnostics;
using HSMDatabase.AccessManager.LargeFiles;
using HSMDatabase.LevelDB.LargeFiles;
using Xunit;
using Xunit.Abstractions;

namespace HSMDatabase.LevelDB.Tests.LargeFileTests
{
    /// <summary>
    /// A kill of the process in the middle of a piece (#1883): a child process appends pieces through the real store and
    /// prints every acknowledged piece; the test kills it at random moments, opens the store and checks that every
    /// acknowledged piece is in the part byte for byte and that the part is cut back to its record. It covers a kill of the
    /// process, not a power loss: the operating system still writes what the process had handed it.
    /// <para>
    /// Alone, never beside other tests: on Windows a child process started with redirected output inherits every
    /// inheritable handle of this process, and LevelDB opens its files (its <c>LOG</c> among them) inheritable, so a child
    /// started while another test has a database open holds that database's files until it exits, and that test cannot
    /// delete its folder.
    /// </para>
    /// </summary>
    [Collection(Name)]
    public sealed class LargeFileStoreKillTests : IDisposable
    {
        public const string Name = "Large file store kill (alone)";

        private const string HostProjectName = "HSMDatabase.LargeFiles.CrashHost";
        private const int PieceSize = 1024 * 1024;
        private const int Pieces = 160;
        private const int Kills = 6;

        private static readonly TimeSpan HostTimeout = TimeSpan.FromSeconds(120);

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"hsm-large-files-kill-{Guid.NewGuid():N}");
        private readonly Guid _product = Guid.NewGuid();
        private readonly Guid _sensor = Guid.NewGuid();
        private readonly ITestOutputHelper _output;


        public LargeFileStoreKillTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_root, recursive: true);
            }
            catch (IOException) { }
        }


        [Fact]
        public void Every_acknowledged_piece_survives_a_kill_in_the_middle_of_a_piece_and_the_upload_continues()
        {
            var random = new Random(1883);
            long acknowledged = -1;
            int kills = 0, midPiece = 0;

            for (var kill = 0; kill < Kills; kill++)
            {
                var run = RunHost(killAfter: TimeSpan.FromMilliseconds(random.Next(30, 250)));

                if (run.Done)
                    break;

                acknowledged = Math.Max(acknowledged, run.LastAck);
                kills++;

                // Bytes past the record: the kill fell inside a piece (after the part write, before the record).
                var tail = TailBeyondRecord();
                midPiece += tail > 0 ? 1 : 0;

                Check(acknowledged);
                _output.WriteLine($"kill {kill}: last acknowledged piece {run.LastAck}, {tail} bytes past the record");
            }

            _output.WriteLine($"{kills} kills, {midPiece} inside a piece");
            Assert.True(kills > 0, "every run finished before its kill: no kill was tested");

            // The last run finishes the file.
            var last = RunHost(killAfter: null);
            Assert.True(last.Done, last.Error);

            var record = Check(Pieces - 1);
            Assert.Equal(Pieces, record.PieceCount);
            Assert.Equal((long)Pieces * PieceSize, record.PartLength);
        }


        // The bytes of the part and the index beyond what the record on the disk covers, before an open cuts them.
        private long TailBeyondRecord()
        {
            var uploads = Path.Combine(_root, LargeFileStore.UploadsFolderName, _product.ToString("N"));
            var path = Assert.Single(Directory.GetFiles(uploads, "*.rec"));
            var record = LargeFileRecordFormat.Read(File.ReadAllText(path));

            return new FileInfo(Path.ChangeExtension(path, ".part")).Length - record.PartLength +
                   new FileInfo(Path.ChangeExtension(path, ".idx")).Length - record.PieceCount * LargeFileStore.IndexEntrySize;
        }

        // Opens the store as a restart would and compares the part with what was acknowledged.
        private LargeFileRecord Check(long lastAcknowledged)
        {
            using var store = new LargeFileStore(_root) { Limits = new LargeFileStoreLimits { MinFreeBytes = 0 } };

            var record = Assert.Single(store.GetFiles());

            Assert.True(record.PieceCount >= lastAcknowledged + 1, $"{record.PieceCount} pieces in the record, piece {lastAcknowledged} was acknowledged");

            var uploads = Path.Combine(_root, LargeFileStore.UploadsFolderName, _product.ToString("N"));
            var part = Path.Combine(uploads, $"{record.FileId:N}.part");
            var index = Path.Combine(uploads, $"{record.FileId:N}.idx");

            // Cut back to the record by the open: nothing beyond it.
            Assert.Equal(record.PartLength, new FileInfo(part).Length);
            Assert.Equal(record.PieceCount * LargeFileStore.IndexEntrySize, new FileInfo(index).Length);
            Assert.Equal(record.PieceCount * PieceSize, record.PartLength);

            using var stream = File.OpenRead(part);
            var buffer = new byte[PieceSize];

            for (long n = 0; n < record.PieceCount; n++)
            {
                stream.ReadExactly(buffer);
                Assert.True(buffer.AsSpan().SequenceEqual(LargeFileCrashContent.Piece(n, PieceSize)), $"piece {n} differs");
            }

            return record;
        }

        private HostRun RunHost(TimeSpan? killAfter)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                ArgumentList = { LocateHost(), _root, _product.ToString(), _sensor.ToString(), Pieces.ToString(), PieceSize.ToString() },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            using var process = new Process { StartInfo = startInfo };
            var lastAck = -1L;
            var done = false;
            var firstAck = new ManualResetEventSlim();
            var error = new System.Text.StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null)
                    return;

                if (e.Data.StartsWith("ACK ", StringComparison.Ordinal))
                {
                    Interlocked.Exchange(ref lastAck, long.Parse(e.Data[4..]));
                    firstAck.Set();
                }
                else if (e.Data == "DONE")
                {
                    done = true;
                    firstAck.Set();
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    lock (error)
                        error.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            if (killAfter is { } delay)
            {
                // At least one piece in, then a moment that falls inside a piece more often than not (a piece of 1 MiB
                // flushed twice takes a few milliseconds and more).
                firstAck.Wait(HostTimeout);
                Thread.Sleep(delay);

                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException) { } // exited already
            }

            Assert.True(process.WaitForExit((int)HostTimeout.TotalMilliseconds), "the crash host did not exit");
            process.WaitForExit(); // the output is read to its end

            // Read after the exit: every line the host printed before it died has been handled.
            return new HostRun(Interlocked.Read(ref lastAck), done, error.ToString());
        }

        private static string LocateHost()
        {
            // The test runs from src/tests/HSMDatabase.LevelDB.Tests/bin/<configuration>/<tfm>/; the host is built next to it.
            var baseDirectory = new DirectoryInfo(AppContext.BaseDirectory);
            var tfm = baseDirectory.Name;
            var configuration = baseDirectory.Parent!.Name;
            var tests = baseDirectory.Parent.Parent!.Parent!.Parent!;
            var host = Path.Combine(tests.FullName, HostProjectName, "bin", configuration, tfm, HostProjectName + ".dll");

            Assert.True(File.Exists(host), $"The crash host was not found at {host}; it is built with HSMDatabase.LevelDB.Tests.");

            return host;
        }


        private sealed record HostRun(long LastAck, bool Done, string Error);
    }
}
