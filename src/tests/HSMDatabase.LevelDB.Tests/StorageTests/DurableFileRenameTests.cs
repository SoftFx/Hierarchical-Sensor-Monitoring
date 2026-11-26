using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HSMDatabase.LevelDB.Sealed;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.StorageTests
{
    // #1883: on Windows the rename of DurableFile fails with error 5 while another process has the target open, even one that
    // shares read, write and delete (a virus scanner or the search indexer reading a record just written), and with error 32
    // while one holds the source. Those two are retried after DurableFile.RenameRetryPauses; any other error is not; past the
    // retries the error of the last attempt is thrown as before. The pause seam makes the holder let go at a known attempt,
    // so nothing here depends on the clock. Linux replaces an open file and retries nothing: the tests are skipped there.
    public sealed class DurableFileRenameTests : IDisposable
    {
        private readonly string _folder = Path.Combine(Path.GetTempPath(), $"hsm-durable-rename-{Guid.NewGuid():N}");
        private readonly string _target;
        private readonly string _temporary;

        private readonly List<TimeSpan> _pauses = [];
        private int _attempts;


        public DurableFileRenameTests()
        {
            Directory.CreateDirectory(_folder);

            _target = Path.Combine(_folder, "record.rec");
            _temporary = _target + ".tmp";

            File.WriteAllText(_target, "old");
            File.WriteAllText(_temporary, "new");
        }


        [OnlyOnWindowsFact]
        public void A_replace_whose_target_a_scanner_holds_succeeds_once_the_scanner_lets_go()
        {
            using var scanner = HoldAsAScannerDoes(_target);

            // The test's reader lets go during the second pause, well inside the bound. From then on the pauses are real
            // ones, so a scanner of the machine reading the fresh files gets the time the retry gives it in production.
            DurableFile.Rename(_temporary, _target, overwrite: true, pause =>
            {
                Pause(pause);

                if (_pauses.Count == 2)
                    scanner.Dispose();
                else if (_pauses.Count > 2)
                    Thread.Sleep(pause);
            }, Attempting);

            Assert.Equal("new", File.ReadAllText(_target));
            Assert.False(File.Exists(_temporary));

            // The first two attempts failed while the test's reader held the target.
            Assert.True(_attempts >= 3, $"{_attempts} attempts");
            Assert.Equal(DurableFile.RenameRetryPauses[.._pauses.Count], _pauses);
        }

        [OnlyOnWindowsFact]
        public void A_replace_held_past_the_last_retry_throws_the_error_of_the_last_attempt_and_leaves_both_files()
        {
            using (HoldAsAScannerDoes(_target))
            {
                var error = Assert.Throws<IOException>(() => DurableFile.Rename(_temporary, _target, overwrite: true, pause => Pause(pause), Attempting));

                // The exception of a rename that is not retried: the same text, the error of the last attempt.
                Assert.StartsWith($"Could not rename {_temporary} to {_target} (error ", error.Message);
                Assert.True(error.Message.EndsWith("(error 5)") || error.Message.EndsWith("(error 32)"), error.Message);
            }

            Assert.Equal(DurableFile.RenameRetryPauses.Length + 1, _attempts);
            Assert.Equal(DurableFile.RenameRetryPauses, _pauses);

            // Nothing is undone or cleaned: the target keeps its old content and the temporary file stays (the next write of
            // the file replaces it; the large file store removes a leftover .rec.tmp when it opens).
            Assert.Equal("old", File.ReadAllText(_target));
            Assert.Equal("new", File.ReadAllText(_temporary));
        }

        [OnlyOnWindowsFact]
        public void A_rename_whose_source_is_missing_is_tried_once()
        {
            File.Delete(_temporary);

            var error = Assert.Throws<IOException>(() => DurableFile.Rename(_temporary, _target, overwrite: true, pause => Pause(pause), Attempting));

            Assert.EndsWith("(error 2)", error.Message);
            Assert.Equal(1, _attempts);
            Assert.Empty(_pauses);
            Assert.Equal("old", File.ReadAllText(_target));
        }

        [OnlyOnWindowsFact]
        public void A_rename_onto_an_existing_file_without_overwrite_is_tried_once()
        {
            // As the hsm-sealed marker and a committed archive are renamed: over nothing, and an existing target is an error.
            var error = Assert.Throws<IOException>(() => DurableFile.Rename(_temporary, _target, overwrite: false, pause => Pause(pause), Attempting));

            Assert.EndsWith("(error 183)", error.Message);
            Assert.Equal(1, _attempts);
            Assert.Empty(_pauses);
            Assert.Equal("old", File.ReadAllText(_target));
            Assert.Equal("new", File.ReadAllText(_temporary));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_folder, recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }


        // A reader that shares everything, as a virus scanner opens a file it reads.
        private static FileStream HoldAsAScannerDoes(string path) =>
            new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        private void Attempting(int attempt)
        {
            Assert.Equal(_attempts, attempt);
            _attempts++;
        }

        private void Pause(TimeSpan pause) => _pauses.Add(pause);


        private sealed class OnlyOnWindowsFactAttribute : FactAttribute
        {
            public OnlyOnWindowsFactAttribute()
            {
                if (!OperatingSystem.IsWindows())
                    Skip = "Windows only: there a reader of the target blocks the replace; on Linux rename(2) replaces an open file, " +
                           "so the error this retry cures cannot be made";
            }
        }
    }
}
