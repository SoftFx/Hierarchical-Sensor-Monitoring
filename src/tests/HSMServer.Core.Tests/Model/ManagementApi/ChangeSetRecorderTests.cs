using System;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.ManagementApi.ChangeSets;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // #1588 review round 4: when the request's undo group went terminal
    // mid-request (a rollback finishing during a multi-sensor import), the
    // remaining writes fork into a fresh group — and that forked group MUST
    // get a journal record for EVERY kind, or its undo points are reachable
    // from no surface (policy cache-layer records still carry the original,
    // terminal group id).
    public class ChangeSetRecorderTests
    {
        [Fact]
        public void Record_WhenTheGroupForks_WritesAJournalRecord_ForPolicyKindsToo()
        {
            var changeSetJournal = new Mock<IChangeSetJournal>();
            var forkedId = Guid.NewGuid();

            changeSetJournal.Setup(j => j.Append(It.IsAny<Guid>(), It.IsAny<InitiatorInfo>(),
                    It.IsAny<ChangeSetEntry>()))
                .Returns(forkedId);

            var journalService = new Mock<HSMServer.Core.Journal.IJournalService>();
            var recorder = new ChangeSetRecorder(changeSetJournal.Object, journalService.Object,
                NullLogger<ChangeSetRecorder>.Instance);

            var landed = recorder.Record(Guid.NewGuid(), InitiatorInfo.AsUser("alice"),
                new ChangeSetEntry
                {
                    Kind = ChangeSetEntryKind.SensorDataPolicies,
                    Operation = ChangeSetOperation.Update,
                    EntityId = Guid.NewGuid(),
                    ParentId = Guid.NewGuid(),
                    Path = "Root/svc",
                });

            Assert.Equal(forkedId, landed);

            journalService.Verify(s => s.AddRecord(It.Is<HSMServer.Core.Model.JournalRecordModel>(r =>
                r.ChangeSetId == forkedId.ToString() &&
                r.Kind == Core.Journal.JournalKinds.SensorDataPolicies &&
                r.ProductId != null)), Times.Once);
        }

        [Fact]
        public void Record_IntoAnOpenGroup_WritesNoJournalRecord_ForPolicyKinds()
        {
            // Policy kinds journal through the cache-layer records; the
            // recorder writes its own record only for template/schedule kinds
            // and for forks.
            var changeSetId = Guid.NewGuid();
            var changeSetJournal = new Mock<IChangeSetJournal>();

            changeSetJournal.Setup(j => j.Append(It.IsAny<Guid>(), It.IsAny<InitiatorInfo>(),
                    It.IsAny<ChangeSetEntry>()))
                .Returns(changeSetId);

            var journalService = new Mock<HSMServer.Core.Journal.IJournalService>();
            var recorder = new ChangeSetRecorder(changeSetJournal.Object, journalService.Object,
                NullLogger<ChangeSetRecorder>.Instance);

            recorder.Record(changeSetId, InitiatorInfo.AsUser("alice"), new ChangeSetEntry
            {
                Kind = ChangeSetEntryKind.SensorDataPolicies,
                Operation = ChangeSetOperation.Update,
                EntityId = Guid.NewGuid(),
                Path = "Root/svc",
            });

            journalService.Verify(s => s.AddRecord(It.IsAny<HSMServer.Core.Model.JournalRecordModel>()), Times.Never);
        }
    }
}
