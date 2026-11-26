using System;
using System.Collections.Generic;
using System.IO;
using HSMDatabase.LevelDB.Sealed;
using Xunit;

namespace HSMDatabase.LevelDB.Tests.StorageTests
{
    // #1883: on Windows the rename of DurableFile fails with error 5 while another process has the target open, even one that
    // shares read, write and delete (a virus scanner or the search indexer reading a record just written), and with error 32
    // while one holds the source. Those two are retried after DurableFile.RenameRetryPauses; any other error is not; past the
    // retries the error of the last attempt is thrown as before. The pause seam makes the holder let go at a known attempt,
    // so nothing here depends on the clock. Linux replaces an open file and retries nothing: the tests return there.
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


        [Fact]
        public void A_replace_whose_target_a_scanner_holds_succeeds_once_the_scanner_lets_go()
        {
            if (!OperatingSystem.IsWindows())
                return;

            var scanner = HoldAsAScannerDoes(_target);

            // The scanner lets go during the second pause: well inside the bound.
            DurableFile.Rename(_temporary, _target, overwrite: true, pause => Pause(pause, () => { if (_pauses.Count == 2) scanner.Dispose(); }), Attempting);

            Assert.Equal("new", File.ReadAllText(_target));
            Assert.False(File.Exists(_temporary));

            // Two attempts failed while it held the file (more only if another process of the machine held it too).
            Assert.True(_attempts >= 3, $"{_attempts} attempts");
            Assert.Equal(DurableFile.RenameRetryPauses[.._pauses.Count], _pauses);
        }

        [Fact]
        public void A_replace_held_past_the_last_retry_throws_the_error_of_the_last_attempt_and_leaves_both_files()
        {
            if (!OperatingSystem.IsWindows())
                return;

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

        [Fact]
        public void A_rename_whose_source_is_missing_is_tried_once()
        {
            if (!OperatingSystem.IsWindows())
                return;

            File.Delete(_temporary);

            var error = Assert.Throws<IOException>(() => DurableFile.Rename(_temporary, _target, overwrite: true, pause => Pause(pause), Attempting));

            Assert.EndsWith("(error 2)", error.Message);
            Assert.Equal(1, _attempts);
            Assert.Empty(_pauses);
            Assert.Equal("old", File.ReadAllText(_target));
        }

        [Fact]
        public void A_rename_onto_an_existing_file_without_overwrite_is_tried_once()
        {
            if (!OperatingSystem.IsWindows())
                return;

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

        private void Pause(TimeSpan pause, Action then = null)
        {
            _pauses.Add(pause);
            then?.Invoke();
        }
    }
}
