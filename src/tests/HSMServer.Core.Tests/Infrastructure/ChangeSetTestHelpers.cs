using System;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.DataLayer;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.ManagementApi.ChangeSets;
using Moq;

namespace HSMServer.Core.Tests.Infrastructure
{
    // #1570: builds the ChangeSetRecorder for suites that construct the
    // administration services directly. Over the real test LevelDB when a
    // database is given (journaling actually lands in the ChangeSets table);
    // over a no-op mock journal for the mocked-cache suites, which test
    // validation and gates, not the undo journal.
    internal static class ChangeSetTestHelpers
    {
        internal static ChangeSetRecorder Recorder() => Recorder(null);

        internal static ChangeSetRecorder Recorder(IDatabaseCore database) =>
            new(database is null ? Journal() : new ChangeSetJournal(database),
                CommonMoqs.CreateNullLogger<ChangeSetRecorder>());

        // The mock accepts every append under the id it was given (the real
        // journal's fork paths — foreign author, terminal set — never trigger
        // here), so the echoed change-set id is the one the test supplied.
        internal static IChangeSetJournal Journal()
        {
            var journal = new Mock<IChangeSetJournal>();

            journal.Setup(j => j.Append(It.IsAny<Guid>(), It.IsAny<InitiatorInfo>(),
                    It.IsAny<ChangeSetEntry>()))
                .Returns((Guid changeSetId, InitiatorInfo _, ChangeSetEntry _) => changeSetId);

            return journal.Object;
        }
    }
}
