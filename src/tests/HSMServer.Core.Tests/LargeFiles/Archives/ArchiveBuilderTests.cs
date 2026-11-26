using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMServer.Core.LargeFiles.Archives;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Archives
{
    // #1885 (ADR-0022 points 12-13): the archive builder over the store seam.
    public sealed class ArchiveBuilderTests : IDisposable
    {
        private const string Password1 = "First-Dump-Password-0001";
        private const string Password2 = "Second-Dump-Password-0002";

        private readonly ArchiveBuilderHarness _h = new();


        public void Dispose() => _h.Dispose();


        [Fact]
        public async Task FirstPack_BuildsAnArchiveThatOpensWithThePassword_AndRemovesThePart()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var content = TestContent.Compressible(5 * 1024 * 1024 + 123);
            var file = _h.Store.AddVerified(_h.Product, content, name: "C:\\dumps\\app", extension: "dmp");

            await _h.Run();

            var record = _h.Store.TryGetFile(file.FileId);
            Assert.Equal(LargeArchiveFileState.Packed, record.State);
            Assert.Equal(1, record.PasswordVersion);
            Assert.NotNull(record.ArchiveId);
            Assert.Equal(ArchiveCommitOutcome.Committed, _h.Store.Commits.Single().Outcome);
            Assert.False(File.Exists(_h.Store.PartPath(file.FileId)));
            Assert.Empty(_h.Store.TempFiles());

            Assert.Equal(content, _h.ReadArchive(file.FileId, Password1));
            using (var stream = File.OpenRead(_h.Store.ArchivePath(record)))
            using (var entry = new SharpZipArchiveWriter().OpenEntry(stream, Password1))
            {
                Assert.Equal("app.dmp", entry.Name);
                Assert.Equal(file.SourceModifiedUtc, entry.EntryTime);
            }

            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
            Assert.Equal(1, _h.Builder.Statistics.Built);
            Assert.Equal(0, _h.Builder.Statistics.Failed);
            Assert.Equal((file.FileId, true, (string)null), _h.Finished.Single());
        }

        [Fact]
        public async Task NoPassword_WaitsForIt_ThenBuildsAfterThePasswordIsSet()
        {
            var content = TestContent.Compressible(300_000);
            var file = _h.Store.AddVerified(_h.Product, content);
            Assert.Equal(ArchiveBuildState.WaitingForPassword, _h.Builder.GetStatus(file.FileId).State);

            await _h.Run();

            Assert.Equal(ArchiveBuildState.WaitingForPassword, _h.Builder.GetStatus(file.FileId).State);
            Assert.Equal(0, _h.Store.BeginCalls);
            Assert.Equal(LargeArchiveFileState.Verified, _h.Store.TryGetFile(file.FileId).State);
            Assert.Empty(_h.Finished);

            _h.Passwords.Set(_h.Product, Password1);
            Assert.Equal(ArchiveBuildState.Queued, _h.Builder.GetStatus(file.FileId).State);

            await _h.Run();

            Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(file.FileId).State);
            Assert.Equal(content, _h.ReadArchive(file.FileId, Password1));
        }

        [Fact]
        public async Task FileVerifiedBeforeARestart_IsFoundWithoutItsEvent()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(100_000), raiseEvent: false);

            await _h.Run();

            Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(file.FileId).State);
        }

        [Fact]
        public async Task NoRoom_WaitsForRoomUntilTheStoreSaysWhen_IsNotAFailure_ThenBuilds()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(200_000));
            _h.Store.BeginOverride = _ => new ArchiveBeginResult(ArchiveBeginOutcome.NoRoom, null, TimeSpan.FromMinutes(10));

            await _h.Run();

            var status = _h.Builder.GetStatus(file.FileId);
            Assert.Equal(ArchiveBuildState.WaitingForRoom, status.State);
            Assert.Equal(_h.Now + TimeSpan.FromMinutes(10), status.NextAttemptUtc);
            Assert.Equal(1, _h.Builder.Statistics.WaitedForRoom);
            Assert.Equal(0, _h.Builder.Statistics.Failed);
            var finished = _h.Finished.Single();
            Assert.False(finished.Ok);
            Assert.Contains("no room", finished.Error);

            _h.Advance(TimeSpan.FromMinutes(9));
            await _h.Run();
            Assert.Equal(1, _h.Store.BeginCalls);

            // Without a time from the store the backoff applies: the second attempt waits 5 minutes.
            _h.Advance(TimeSpan.FromMinutes(1));
            _h.Store.BeginOverride = _ => new ArchiveBeginResult(ArchiveBeginOutcome.NoRoom, null, null);
            await _h.Run();
            Assert.Equal(2, _h.Store.BeginCalls);
            Assert.Equal(_h.Now + TimeSpan.FromMinutes(5), _h.Builder.GetStatus(file.FileId).NextAttemptUtc);
            Assert.Equal(2, _h.Builder.Statistics.WaitedForRoom);

            _h.Store.BeginOverride = null;
            _h.Advance(TimeSpan.FromMinutes(5));
            await _h.Run();

            Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(file.FileId).State);
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
            Assert.Equal(0, _h.Builder.Statistics.Failed);
        }

        [Fact]
        public async Task PartOfAnotherLength_IsNeverBuilt_FailsAndIsRetriedWithTheBackoff()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var content = TestContent.Compressible(400_000);
            var file = _h.Store.AddVerified(_h.Product, content);
            _h.Store.PartLengthOverride = _ => file.PartLength + 1;

            await _h.Run();

            var status = _h.Builder.GetStatus(file.FileId);
            Assert.Equal(ArchiveBuildState.Failed, status.State);
            Assert.Equal(1, status.Attempts);
            Assert.Equal(_h.Now + TimeSpan.FromMinutes(1), status.NextAttemptUtc);
            Assert.Contains("part", status.LastError);
            Assert.Equal(0, _h.Store.BeginCalls);
            Assert.True(File.Exists(_h.Store.PartPath(file.FileId)));
            Assert.Equal(1, _h.Builder.Statistics.Failed);
            Assert.Equal((file.FileId, false, status.LastError), _h.Finished.Single());

            await _h.Run(); // not due yet
            Assert.Equal(1, _h.Builder.GetStatus(file.FileId).Attempts);

            _h.Advance(TimeSpan.FromMinutes(1));
            await _h.Run();
            status = _h.Builder.GetStatus(file.FileId);
            Assert.Equal(2, status.Attempts);
            Assert.Equal(_h.Now + TimeSpan.FromMinutes(5), status.NextAttemptUtc);

            _h.Store.PartLengthOverride = null;
            _h.Advance(TimeSpan.FromMinutes(5));
            await _h.Run();

            Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(file.FileId).State);
            Assert.Equal(content, _h.ReadArchive(file.FileId, Password1));
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
        }

        [Fact]
        public async Task Backoff_GoesThroughTheListThenHourly()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(10_000));
            _h.Store.PartLengthOverride = _ => -1;

            var expected = new[] { 1, 5, 30, 60, 60, 60 };
            foreach (var minutes in expected)
            {
                await _h.Run();
                var next = _h.Builder.GetStatus(file.FileId).NextAttemptUtc.Value;
                Assert.Equal(TimeSpan.FromMinutes(minutes), next - _h.Now);
                _h.Now = next;
            }

            Assert.Equal(expected.Length, _h.Builder.GetStatus(file.FileId).Attempts);
        }

        [Fact]
        public async Task DamagedPart_FailsWithoutAnArchive_AndKeepsThePart()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Random(300_000, seed: 5));
            var bytes = File.ReadAllBytes(_h.Store.PartPath(file.FileId));
            bytes[bytes.Length / 2] ^= 0xFF;
            bytes[bytes.Length / 2 + 1] ^= 0xFF;
            File.WriteAllBytes(_h.Store.PartPath(file.FileId), bytes);

            await _h.Run();

            var status = _h.Builder.GetStatus(file.FileId);
            Assert.Equal(ArchiveBuildState.Failed, status.State);
            Assert.Equal(LargeArchiveFileState.Verified, _h.Store.TryGetFile(file.FileId).State);
            Assert.True(File.Exists(_h.Store.PartPath(file.FileId)));
            Assert.Single(_h.Store.Abandoned);
            Assert.Empty(_h.Store.TempFiles());
            Assert.Empty(_h.Store.Commits);
        }

        [Fact]
        public async Task WriterFault_AbandonsTheTemporaryArchive_KeepsThePart_AndIsRetried()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var content = TestContent.Compressible(250_000);
            var file = _h.Store.AddVerified(_h.Product, content);
            _h.Writer.FailWrite = _ => new InvalidOperationException("disk exploded");

            await _h.Run();

            var status = _h.Builder.GetStatus(file.FileId);
            Assert.Equal(ArchiveBuildState.Failed, status.State);
            Assert.Contains("archive writer failed (InvalidOperationException)", status.LastError);
            Assert.Single(_h.Store.Abandoned);
            Assert.Empty(_h.Store.TempFiles());
            Assert.True(File.Exists(_h.Store.PartPath(file.FileId)));
            Assert.Equal(LargeArchiveFileState.Verified, _h.Store.TryGetFile(file.FileId).State);

            _h.Writer.FailWrite = null;
            _h.Advance(TimeSpan.FromMinutes(1));
            await _h.Run();

            Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(file.FileId).State);
            Assert.Equal(content, _h.ReadArchive(file.FileId, Password1));
        }

        // What .NET reports for a full volume: ERROR_DISK_FULL and ERROR_HANDLE_DISK_FULL as HRESULTs on Windows, the raw
        // errno ENOSPC (28) on Linux (observed with .NET 8 writing to a full tmpfs).
        [Theory]
        [InlineData(unchecked((int)0x80070070))]
        [InlineData(unchecked((int)0x80070027))]
        [InlineData(28)]
        public async Task AFullVolumeWhileWritingTheTemporaryArchive_WaitsForRoom_AbandonsIt_AndIsNotAFailure(int hresult)
        {
            _h.Passwords.Set(_h.Product, Password1);
            var content = TestContent.Compressible(250_000);
            var file = _h.Store.AddVerified(_h.Product, content);
            _h.Writer.FailWrite = _ => new IOException("There is not enough space on the disk.", hresult);

            await _h.Run();

            var status = _h.Builder.GetStatus(file.FileId);
            Assert.Equal(ArchiveBuildState.WaitingForRoom, status.State);
            Assert.Equal(_h.Now + TimeSpan.FromMinutes(1), status.NextAttemptUtc);
            Assert.Equal(1, _h.Builder.Statistics.WaitedForRoom);
            Assert.Equal(0, _h.Builder.Statistics.Failed);
            Assert.Contains("no room", _h.Finished.Single().Error);
            Assert.Single(_h.Store.Abandoned);
            Assert.Empty(_h.Store.TempFiles());
            Assert.Equal(LargeArchiveFileState.Verified, _h.Store.TryGetFile(file.FileId).State);

            _h.Writer.FailWrite = null;
            _h.Advance(TimeSpan.FromMinutes(1));
            await _h.Run();

            Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(file.FileId).State);
            Assert.Equal(content, _h.ReadArchive(file.FileId, Password1));
        }

        [Fact]
        public async Task AnotherIOFaultWhileWriting_IsAFailure()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(250_000));
            _h.Writer.FailWrite = _ => new IOException("The device is not ready.", unchecked((int)0x80070015));

            await _h.Run();

            Assert.Equal(ArchiveBuildState.Failed, _h.Builder.GetStatus(file.FileId).State);
            Assert.Equal(0, _h.Builder.Statistics.WaitedForRoom);
            Assert.Equal(1, _h.Builder.Statistics.Failed);
            Assert.Single(_h.Store.Abandoned);
        }

        [Fact]
        public async Task CommitFault_DuringAReencryption_AbandonsTheBuild_KeepsTheOldArchive_AndIsRetried()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var content = TestContent.Compressible(120_000);
            var file = _h.Store.AddVerified(_h.Product, content);
            await _h.Run();
            var first = _h.Store.TryGetFile(file.FileId).ArchiveId;

            _h.Passwords.Set(_h.Product, Password2);
            _h.Store.BeforeCommit = _ => throw new IOException("the rename failed");

            await _h.Run();

            Assert.Equal(ArchiveBuildState.Failed, _h.Builder.GetStatus(file.FileId).State);
            Assert.Single(_h.Store.Abandoned); // the store's reservation and temporary file go with it
            Assert.Empty(_h.Store.TempFiles());
            Assert.Equal(first, _h.Store.TryGetFile(file.FileId).ArchiveId);
            Assert.Equal(content, _h.ReadArchive(file.FileId, Password1));

            _h.Store.BeforeCommit = null;
            _h.Advance(TimeSpan.FromMinutes(1));
            await _h.Run();

            Assert.NotEqual(first, _h.Store.TryGetFile(file.FileId).ArchiveId);
            Assert.Equal(content, _h.ReadArchive(file.FileId, Password2));
        }

        [Fact]
        public async Task FileRemovedDuringTheBuild_TheCommitSaysRemoved_AndTheFileIsDroppedWithoutRetry()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(150_000));
            _h.Writer.BeforeWrite = _ => _h.Store.Remove(file.FileId);

            await _h.Run();

            Assert.Equal(ArchiveCommitOutcome.Removed, _h.Store.Commits.Single().Outcome);
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
            Assert.Empty(_h.Finished);
            Assert.Equal(0, _h.Builder.Statistics.Failed);
            Assert.Empty(_h.Store.TempFiles());

            _h.Advance(TimeSpan.FromHours(2));
            await _h.Run();
            Assert.Equal(1, _h.Store.BeginCalls);
        }

        [Fact]
        public async Task FileRemovedDuringABuildThatFails_IsDroppedWithoutRetry()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(150_000));
            _h.Writer.FailWrite = _ =>
            {
                _h.Store.Remove(file.FileId);
                return new IOException("the part vanished");
            };

            await _h.Run();

            Assert.Single(_h.Store.Abandoned);
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
            Assert.Empty(_h.Finished);
            Assert.Equal(0, _h.Builder.Statistics.Failed);

            _h.Advance(TimeSpan.FromHours(2));
            await _h.Run();
            Assert.Equal(1, _h.Store.BeginCalls);
        }

        [Fact]
        public async Task BeginNotFoundOrWrongState_DropsTheFileWithoutRetry()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var gone = _h.Store.AddVerified(_h.Product, TestContent.Compressible(10_000));
            var odd = _h.Store.AddVerified(_h.Product, TestContent.Compressible(10_000));
            // The record changes between the builder's read and the begin.
            _h.Store.BeginOverride = r =>
            {
                if (r.FileId == gone.FileId)
                {
                    _h.Store.Remove(r.FileId);
                    return new ArchiveBeginResult(ArchiveBeginOutcome.NotFound, null, null);
                }

                _h.Store.Update(r.FileId, x => x with { State = LargeArchiveFileState.Uploading });
                return new ArchiveBeginResult(ArchiveBeginOutcome.WrongState, null, null);
            };

            await _h.Run();

            Assert.Equal(2, _h.Store.BeginCalls);
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(gone.FileId).State);
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(odd.FileId).State);
            Assert.Empty(_h.Finished);
            Assert.Equal(0, _h.Builder.Statistics.Failed);

            _h.Advance(TimeSpan.FromHours(2));
            await _h.Run();
            Assert.Equal(2, _h.Store.BeginCalls);
        }

        [Fact]
        public async Task CommitSuperseded_ChangesNothing_IsNotAFailure_AndTheFileIsLookedAtAgain()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var content = TestContent.Compressible(120_000);
            var file = _h.Store.AddVerified(_h.Product, content);

            // Another build commits first: the record is packed under another archive when this commit comes.
            var other = Guid.NewGuid();
            _h.Store.BeforeCommit = _ =>
            {
                _h.Store.BeforeCommit = null;
                _h.Store.Update(file.FileId, r => r with { State = LargeArchiveFileState.Packed, ArchiveId = other, PasswordVersion = 1 });
            };

            await _h.Run();

            Assert.Equal(ArchiveCommitOutcome.Superseded, _h.Store.Commits.Single().Outcome);
            Assert.Equal(other, _h.Store.TryGetFile(file.FileId).ArchiveId);
            Assert.Equal(0, _h.Builder.Statistics.Failed);
            Assert.Equal(0, _h.Builder.Statistics.Built);
            Assert.Empty(_h.Finished);
            Assert.Empty(_h.Store.TempFiles());
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
        }

        [Fact]
        public async Task CommitSupersededDuringAReencryption_LeavesTheOtherArchive_AndThePassTakesItFromThere()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var content = TestContent.Compressible(120_000);
            var file = _h.Store.AddVerified(_h.Product, content);
            await _h.Run();
            var firstArchive = _h.Store.TryGetFile(file.FileId).ArchiveId;

            _h.Passwords.Set(_h.Product, Password2);
            var expectedSeen = (Guid?)null;
            var other = Guid.NewGuid();
            _h.Store.BeforeCommit = target =>
            {
                expectedSeen = target.ExpectedArchiveId;
                _h.Store.BeforeCommit = null;
                // Another build re-encrypted the file first.
                File.Delete(_h.Store.ArchivePath(_h.Store.TryGetFile(file.FileId)));
                _h.Store.Update(file.FileId, r => r with { ArchiveId = other, PasswordVersion = 2 });
            };

            await _h.Run();

            Assert.Equal(firstArchive, expectedSeen);
            Assert.Equal(ArchiveCommitOutcome.Superseded, _h.Store.Commits[1].Outcome);
            Assert.Equal(2, _h.Store.Commits.Count);
            Assert.Equal(other, _h.Store.TryGetFile(file.FileId).ArchiveId);
            Assert.Equal(1, _h.Builder.Statistics.Built);
            Assert.Equal(0, _h.Builder.Statistics.Reencrypted);
            Assert.Equal(0, _h.Builder.Statistics.Failed);
            Assert.Empty(_h.Store.TempFiles());
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
            Assert.Equal(1, _h.Passwords.Retired.Single().Version); // no record carries version 1 any more
        }

        [Fact]
        public async Task Reencryption_WritesANewArchiveThatOpensOnlyWithTheNewPassword_AndRemovesTheOldOne()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var content = TestContent.Compressible(600_000);
            var file = _h.Store.AddVerified(_h.Product, content);
            await _h.Run();
            var packed = _h.Store.TryGetFile(file.FileId);
            var oldPath = _h.Store.ArchivePath(packed);

            _h.Passwords.Set(_h.Product, Password2);
            await _h.Run();

            var record = _h.Store.TryGetFile(file.FileId);
            Assert.Equal(2, record.PasswordVersion);
            Assert.NotEqual(packed.ArchiveId, record.ArchiveId);
            Assert.False(File.Exists(oldPath));
            Assert.Single(_h.Store.ArchiveFiles());
            Assert.Equal(content, _h.ReadArchive(file.FileId, Password2));
            Assert.Throws<ArchiveFormatException>(() => _h.ReadArchive(file.FileId, Password1));
            Assert.Equal(1, _h.Builder.Statistics.Reencrypted);
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);

            Assert.Equal((_h.Product, 1), _h.Passwords.Retired.Select(r => (r.ProductId, r.Version)).Single());
            Assert.Equal(1, _h.Builder.Statistics.PasswordsRetired);
        }

        [Fact]
        public async Task Reencryption_WithTheOldVersionLost_FailsWithAClearError_AndKeepsTheOldArchive()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(50_000));
            await _h.Run();
            var packed = _h.Store.TryGetFile(file.FileId);

            _h.Passwords.Set(_h.Product, Password2);
            _h.Passwords.Lose(_h.Product, 1);
            await _h.Run();

            var status = _h.Builder.GetStatus(file.FileId);
            Assert.Equal(ArchiveBuildState.Failed, status.State);
            Assert.Contains("version 1", status.LastError);
            Assert.True(File.Exists(_h.Store.ArchivePath(packed)));
            Assert.Equal(packed, _h.Store.TryGetFile(file.FileId));
        }

        [Fact]
        public async Task PackedArchiveOfAProductWhosePasswordsCannotBeRead_IsLeftAlone_WithoutAReport()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(50_000));
            await _h.Run();
            var packed = _h.Store.TryGetFile(file.FileId);
            _h.Finished.Clear();

            // The stored row of the product cannot be read: the service keeps no version of it.
            _h.Passwords.Lose(_h.Product, 1);
            _h.Passwords.Unreadable.Add(_h.Product);
            await _h.Run();
            await _h.Run();

            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
            Assert.Empty(_h.Finished);
            Assert.Equal(packed, _h.Store.TryGetFile(file.FileId));
        }

        [Fact]
        public async Task PackedArchiveWhoseVersionIsNotKept_FailsVisiblyOnce_AndIsNeverRetriedByItself()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(50_000));
            await _h.Run();
            var packed = _h.Store.TryGetFile(file.FileId);
            _h.Finished.Clear();

            _h.Passwords.Set(_h.Product, Password2);
            _h.Passwords.Lose(_h.Product, 1);
            var writes = _h.Writer.Writes;

            for (var pass = 0; pass < 5; pass++)
            {
                await _h.Run();
                _h.Advance(TimeSpan.FromHours(2));
            }

            const string error = "the password version 1 of this archive is not kept";
            var status = _h.Builder.GetStatus(file.FileId);
            Assert.Equal(ArchiveBuildState.Failed, status.State);
            Assert.Equal(error, status.LastError);
            Assert.Null(status.NextAttemptUtc);
            Assert.Equal((file.FileId, false, error), _h.Finished.Single());
            Assert.Equal(1, _h.Builder.Statistics.Failed);
            Assert.Equal(writes, _h.Writer.Writes);
            Assert.Equal(packed, _h.Store.TryGetFile(file.FileId));
            Assert.True(File.Exists(_h.Store.ArchivePath(packed)));
        }

        [Fact]
        public async Task PackedArchiveOfAProductWithNoKeptVersionLeft_FailsVisiblyOnce()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(20_000));
            await _h.Run();
            _h.Finished.Clear();

            _h.Passwords.Lose(_h.Product, 1);
            await _h.Run();
            _h.Advance(TimeSpan.FromHours(2));
            await _h.Run();

            const string error = "the password version 1 of this archive is not kept";
            Assert.Equal(error, _h.Builder.GetStatus(file.FileId).LastError);
            Assert.Equal((file.FileId, false, error), _h.Finished.Single());
        }

        [Fact]
        public async Task PackedArchiveNewerThanTheCurrentVersion_FailsVisiblyOnce_AndIsNotRewritten()
        {
            _h.Passwords.Set(_h.Product, Password1);
            _h.Passwords.Set(_h.Product, Password2);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(20_000));
            await _h.Run();
            Assert.Equal(2, _h.Store.TryGetFile(file.FileId).PasswordVersion);
            _h.Finished.Clear();

            _h.Passwords.PinCurrent(_h.Product, 1);
            var writes = _h.Writer.Writes;
            for (var pass = 0; pass < 3; pass++)
            {
                await _h.Run();
                _h.Advance(TimeSpan.FromHours(2));
            }

            var status = _h.Builder.GetStatus(file.FileId);
            Assert.Equal(ArchiveBuildState.Failed, status.State);
            Assert.Contains("version 2", status.LastError);
            Assert.Null(status.NextAttemptUtc);
            Assert.Equal((file.FileId, false, status.LastError), _h.Finished.Single());
            Assert.Equal(writes, _h.Writer.Writes);
            Assert.Equal(2, _h.Store.TryGetFile(file.FileId).PasswordVersion);
        }

        [Fact]
        public async Task PasswordChangeDuringABuild_CommitsUnderTheVersionTaken_ThenReencrypts()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var content = TestContent.Compressible(300_000);
            var file = _h.Store.AddVerified(_h.Product, content);

            _h.Store.BeforeCommit = _ =>
            {
                _h.Store.BeforeCommit = null;
                _h.Passwords.Set(_h.Product, Password2);
            };

            await _h.Run();
            await _h.Run();

            Assert.Equal(new[] { (file.FileId, 1, ArchiveCommitOutcome.Committed), (file.FileId, 2, ArchiveCommitOutcome.Committed) }, _h.Store.Commits.ToArray());
            Assert.Equal(2, _h.Store.TryGetFile(file.FileId).PasswordVersion);
            Assert.Equal(content, _h.ReadArchive(file.FileId, Password2));
            Assert.Equal(1, _h.Builder.Statistics.Built);
            Assert.Equal(1, _h.Builder.Statistics.Reencrypted);
            Assert.Equal(1, _h.Passwords.Retired.Single().Version);
        }

        [Fact]
        public async Task PasswordVersion_IsRetiredOnlyAfterTheLastArchiveOfItIsRewritten_AndOnlyOnce()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var a = _h.Store.AddVerified(_h.Product, TestContent.Compressible(100_000, seed: 1));
            var b = _h.Store.AddVerified(_h.Product, TestContent.Compressible(100_000, seed: 2));
            await _h.Run();
            Assert.Empty(_h.Passwords.Retired);

            // A product without files: its replaced version is retired at the end of the pass.
            var lonely = Guid.NewGuid();
            _h.Passwords.Set(lonely, Password1);
            _h.Passwords.Set(lonely, Password2);

            _h.Passwords.Set(_h.Product, Password2);
            var retiredDuringBuilds = 0;
            _h.Writer.BeforeWrite = _ => retiredDuringBuilds += _h.Passwords.GetVersion(_h.Product, 1).IsRetired ? 1 : 0;
            _h.Store.BeforeCommit = _ => retiredDuringBuilds += _h.Passwords.GetVersion(_h.Product, 1).IsRetired ? 1 : 0;

            await _h.Run();

            Assert.Equal(0, retiredDuringBuilds);
            Assert.Equal(2, _h.Builder.Statistics.Reencrypted);
            Assert.Equal(
                new[] { (_h.Product, 1), (lonely, 1) },
                _h.Passwords.Retired.Select(r => (r.ProductId, r.Version)).OrderBy(r => r.ProductId == lonely).ToArray());
            Assert.All(_h.Passwords.Retired, r => Assert.Equal(_h.Now, r.Utc));

            await _h.Run();
            Assert.Equal(2, _h.Passwords.Retired.Count);
            Assert.Equal(2, _h.Builder.Statistics.PasswordsRetired);

            // The service owns the retention; the builder asks it after every pass.
            Assert.Equal(3, _h.Passwords.RemoveExpiredCalls);
            _h.Advance(TimeSpan.FromDays(31));
            await _h.Run();
            Assert.Null(_h.Passwords.GetVersion(_h.Product, 1));
            Assert.NotNull(_h.Passwords.GetVersion(_h.Product, 2));
            Assert.Equal(a.ProductId, b.ProductId);
        }

        [Fact]
        public async Task TheRemovalOfExpiredVersions_IsGivenTheVersionsThePackedArchivesCarry()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(100_000));
            await _h.Run();

            // A version retired although its archive was never rewritten (the re-encryption keeps failing): the removal
            // after the retention still sees that an archive carries it, and keeps it.
            _h.Passwords.Set(_h.Product, Password2);
            _h.Writer.FailWrite = _ => new InvalidOperationException("the writer is broken");
            _h.Passwords.MarkRetired(_h.Product, 1, _h.Now);
            _h.Advance(TimeSpan.FromDays(31));

            await _h.Run();

            Assert.Equal(ArchiveBuildState.Failed, _h.Builder.GetStatus(file.FileId).State);
            Assert.Contains((_h.Product, 1), _h.Passwords.LastCarried);
            Assert.NotNull(_h.Passwords.GetVersion(_h.Product, 1));
            Assert.Equal(TestContent.Compressible(100_000), _h.ReadArchive(file.FileId, Password1));
        }

        [Fact]
        public async Task ConcurrentPasses_NeverRunTwoBuildsAtOnce()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var files = Enumerable.Range(0, 4).Select(i => _h.Store.AddVerified(_h.Product, TestContent.Compressible(100_000, seed: i))).ToList();
            _h.Writer.BeforeWrite = _ => Thread.Sleep(30);

            await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(_h.Run)));

            Assert.Equal(1, _h.Writer.MaxConcurrentWrites);
            Assert.Equal(4, _h.Writer.Writes);
            Assert.Equal(4, _h.Builder.Statistics.Built);
            Assert.All(files, f => Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(f.FileId).State));
        }

        [Fact]
        public async Task Cancellation_AbandonsTheBuild_KeepsThePart_AndCountsNoFailure()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(3 << 20));
            using var cts = new CancellationTokenSource();
            _h.Writer.BeforeWrite = _ => cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _h.Builder.RunDueAsync(cts.Token));

            Assert.Single(_h.Store.Abandoned);
            Assert.Empty(_h.Store.TempFiles());
            Assert.True(File.Exists(_h.Store.PartPath(file.FileId)));
            Assert.Equal(ArchiveBuildState.Queued, _h.Builder.GetStatus(file.FileId).State);
            Assert.Equal(0, _h.Builder.Statistics.Failed);

            _h.Writer.BeforeWrite = null;
            await _h.Run();
            Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(file.FileId).State);
        }

        [Fact]
        public async Task NotSecure_IsPackedAtOnceWithoutAPassword_AndOpensOnlyWithoutOne()
        {
            var content = TestContent.Compressible(700_000);
            var file = _h.Store.AddVerified(_h.Product, content, secure: false);

            // The product has no password: a file that is not secure never waits for one.
            Assert.Equal(ArchiveBuildState.Queued, _h.Builder.GetStatus(file.FileId).State);

            await _h.Run();

            var record = _h.Store.TryGetFile(file.FileId);
            Assert.Equal(LargeArchiveFileState.Packed, record.State);
            Assert.False(record.IsSecure);
            Assert.Null(record.PasswordVersion);
            Assert.Equal((file.FileId, 0, ArchiveCommitOutcome.Committed), _h.Store.Commits.Single());
            Assert.False(File.Exists(_h.Store.PartPath(file.FileId)));

            Assert.Equal(content, _h.ReadArchive(file.FileId, null));
            Assert.Throws<ArchiveFormatException>(() => _h.ReadArchive(file.FileId, Password1));

            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
            Assert.Equal(1, _h.Builder.Statistics.Built);
            Assert.Equal(1, _h.Builder.Statistics.BuiltNotSecure);
            Assert.Equal(0, _h.Builder.Statistics.BuiltSecure);
            Assert.Equal((file.FileId, true, (string)null), _h.Finished.Single());
            Assert.Contains(_h.LogLines, line => line.Contains("without encryption"));
        }

        [Fact]
        public async Task NotSecure_OfAProductWithAPassword_IsUnencrypted_AndAPasswordChangeNeverRewritesIt()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var open = _h.Store.AddVerified(_h.Product, TestContent.Compressible(200_000, seed: 1), secure: false);
            var secret = _h.Store.AddVerified(_h.Product, TestContent.Compressible(200_000, seed: 2));

            await _h.Run();

            var packedOpen = _h.Store.TryGetFile(open.FileId);
            Assert.Null(packedOpen.PasswordVersion);
            Assert.Equal(1, _h.Store.TryGetFile(secret.FileId).PasswordVersion);
            Assert.Equal(1, _h.Builder.Statistics.BuiltSecure);
            Assert.Equal(1, _h.Builder.Statistics.BuiltNotSecure);

            _h.Passwords.Set(_h.Product, Password2);
            await _h.Run();

            // The secure archive is re-encrypted; the one that is not secure stays the same file. Version 1 is retired once
            // the secure archive left it: the archive that is not secure carries no version.
            Assert.Equal(packedOpen, _h.Store.TryGetFile(open.FileId));
            Assert.Equal(1, _h.Builder.Statistics.Reencrypted);
            Assert.Equal(2, _h.Store.TryGetFile(secret.FileId).PasswordVersion);
            Assert.Equal(1, _h.Passwords.Retired.Single().Version);
            Assert.Equal(TestContent.Compressible(200_000, seed: 1), _h.ReadArchive(open.FileId, null));
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(open.FileId).State);
        }

        [Fact]
        public async Task NotSecure_IsIgnoredByTheVersionRetirement_AndByTheVersionNotKeptReport()
        {
            _h.Passwords.Set(_h.Product, Password1);
            _h.Passwords.Set(_h.Product, Password2);
            var file = _h.Store.AddVerified(_h.Product, TestContent.Compressible(20_000), secure: false);
            await _h.Run();
            _h.Finished.Clear();

            // A record of a file that is not secure that names a version anyway (a store that keeps one) carries nothing:
            // version 1 is retired and no "version not kept" failure is reported for it.
            _h.Store.Update(file.FileId, r => r with { PasswordVersion = 1 });
            _h.Passwords.Lose(_h.Product, 1);
            await _h.Run();
            _h.Passwords.Set(_h.Product, "Third-Dump-Password-00003");
            await _h.Run();

            Assert.Empty(_h.Finished);
            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(file.FileId).State);
            Assert.Equal(0, _h.Builder.Statistics.Failed);
            Assert.Equal(0, _h.Builder.Statistics.Reencrypted);
            Assert.Contains(_h.Passwords.Retired, r => r.Version == 2);
        }

        [Fact]
        public async Task ManyTinyNotSecureFiles_ArePackedInOnePass_FromOneListing()
        {
            const int count = 2_000;
            var files = Enumerable.Range(0, count)
                .Select(i => _h.Store.AddVerified(_h.Product, TestContent.Random(64 + i % 32, seed: i), name: $"f{i}.txt", extension: "txt", secure: false))
                .ToList();

            await _h.Run();

            Assert.All(files, f => Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(f.FileId).State));
            Assert.Equal(count, _h.Builder.Statistics.BuiltNotSecure);
            Assert.Equal(count, _h.Writer.Writes);

            // Two listings (verified, packed) to start the pass and one listing of the packed files to retire versions:
            // never one per build.
            Assert.True(_h.Store.GetFilesCalls <= 5, $"{_h.Store.GetFilesCalls} listings of the store in one pass");
            Assert.Equal(0, _h.Builder.TrackedCount);
        }

        [Fact]
        public async Task AFileVerifiedDuringAPass_IsBuiltInTheSamePass()
        {
            _h.Passwords.Set(_h.Product, Password1);
            var first = _h.Store.AddVerified(_h.Product, TestContent.Compressible(20_000, seed: 1));
            LargeArchiveFile late = null;
            _h.Writer.BeforeWrite = _ =>
            {
                _h.Writer.BeforeWrite = null;
                late = _h.Store.AddVerified(_h.Product, TestContent.Compressible(20_000, seed: 2), secure: false);
            };

            await _h.Run();

            Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(first.FileId).State);
            Assert.Equal(LargeArchiveFileState.Packed, _h.Store.TryGetFile(late.FileId).State);
        }

        [Fact]
        public async Task TheStatusOfAFileTheStoreNoLongerKnows_IsDropped()
        {
            var waiting = _h.Store.AddVerified(_h.Product, TestContent.Compressible(10_000));
            await _h.Run();
            Assert.Equal(ArchiveBuildState.WaitingForPassword, _h.Builder.GetStatus(waiting.FileId).State);
            Assert.Equal(1, _h.Builder.TrackedCount);

            _h.Store.Remove(waiting.FileId);
            await _h.Run();

            Assert.Equal(ArchiveBuildState.None, _h.Builder.GetStatus(waiting.FileId).State);
            Assert.Equal(0, _h.Builder.TrackedCount);
        }

        [Fact]
        public async Task Password_NeverAppearsInStatusesEventsOrLogs()
        {
            const string secret = "Recognisable-SECRET-Pa55word-7731";
            const string secret2 = "Another-SECRET-Pa55word-9902";
            var product2 = Guid.NewGuid();
            _h.Passwords.Set(_h.Product, secret);
            _h.Passwords.Set(product2, secret);

            // A part of another length.
            var shortPart = _h.Store.AddVerified(_h.Product, TestContent.Compressible(20_000));
            _h.Store.PartLengthOverride = id => id == shortPart.FileId ? 1 : null;

            // A writer whose exception text carries the password.
            var leaky = _h.Store.AddVerified(product2, TestContent.Compressible(20_000));
            _h.Writer.FailWrite = password => new InvalidOperationException($"cannot encrypt with '{password}'", new Exception(password));

            // A handler of the event that throws with the password.
            _h.Builder.BuildFinished += (_, _, _) => throw new Exception(secret);

            await _h.Run();

            // A packed file whose old version is lost, then a good build and a re-encryption.
            _h.Writer.FailWrite = null;
            _h.Store.PartLengthOverride = null;
            _h.Advance(TimeSpan.FromMinutes(1));
            await _h.Run();
            _h.Passwords.Set(_h.Product, secret2);
            _h.Passwords.Set(product2, secret2);
            _h.Passwords.Lose(product2, 1);
            await _h.Run();

            Assert.Equal(2, _h.Builder.Statistics.Built);
            Assert.Equal(1, _h.Builder.Statistics.Reencrypted);
            Assert.True(_h.Builder.Statistics.Failed >= 3);

            var texts = _h.Finished.Select(f => f.Error)
                .Concat([_h.Builder.GetStatus(shortPart.FileId).LastError, _h.Builder.GetStatus(leaky.FileId).LastError])
                .Concat(_h.LogLines)
                .Where(t => t is not null)
                .ToList();

            Assert.NotEmpty(_h.LogLines);
            Assert.Contains(texts, t => t.Contains("archive writer failed"));
            Assert.Contains(texts, t => t.Contains("version 1"));
            Assert.All(texts, t =>
            {
                Assert.DoesNotContain(secret, t);
                Assert.DoesNotContain(secret2, t);
            });
        }
    }
}
