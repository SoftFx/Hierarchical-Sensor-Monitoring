using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Xunit;

namespace HSMServer.Core.Tests.ChangeSets
{
    // The undo-journal persistence seam (#1570): append/group/status/cleanup
    // against the REAL test LevelDB (the ChangeSets table), exactly like the
    // JournalService suites. Service-level behavior (journaling on writes,
    // rollback semantics) lives in the administration-service suites.
    public sealed class ChangeSetJournalTests : MonitoringCoreTestsBase<ChangeSetJournalTests.ChangeSetJournalFixture>
    {
        private readonly IChangeSetJournal _journal;

        public ChangeSetJournalTests(ChangeSetJournalFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false) =>
            _journal = new ChangeSetJournal(_databaseCoreManager.DatabaseCore);


        [Fact]
        public void Append_SameAuthorGroups_SequenceFollowsAppendOrder()
        {
            var id = Guid.NewGuid();

            var firstLanding = _journal.Append(id, InitiatorInfo.AsUser("alice"),
                Entry(ChangeSetEntryKind.SensorDataPolicies));
            var secondLanding = _journal.Append(id, InitiatorInfo.AsUser("alice"),
                Entry(ChangeSetEntryKind.SensorTtlPolicies));

            Assert.Equal(id, firstLanding);
            Assert.Equal(id, secondLanding);

            var stored = _journal.Get(id);

            Assert.NotNull(stored);
            Assert.Equal("alice", stored.Author); // the FIRST write fixes the author
            Assert.Equal(InitiatorType.User, stored.AuthorType);
            Assert.Equal(ChangeSetStatus.Applied, stored.Status);
            Assert.Equal([0, 1], stored.Entries.Select(e => e.Sequence));
            Assert.Equal(ChangeSetEntryKind.SensorDataPolicies, stored.Entries[0].Kind);
            Assert.Equal(ChangeSetEntryKind.SensorTtlPolicies, stored.Entries[1].Kind);
        }

        [Fact]
        public void Append_ForeignAuthor_ForksToAFreshId()
        {
            // The fork rule: a change set accepts appends only from its own
            // author — a foreign entry would distort the audit trail and
            // block the author's rollback pre-flight, so it journals under a
            // fresh id the echo names instead.
            var id = Guid.NewGuid();

            _journal.Append(id, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.SensorDataPolicies));

            var forked = _journal.Append(id, InitiatorInfo.AsUser("bob"), Entry(ChangeSetEntryKind.SensorTtlPolicies));

            Assert.NotEqual(id, forked);

            var original = _journal.Get(id);
            var forkSet = _journal.Get(forked);

            Assert.Equal("alice", original.Author);
            Assert.Single(original.Entries);
            Assert.Equal("bob", forkSet.Author);
            Assert.Single(forkSet.Entries);
        }

        [Fact]
        public void Append_DistinctIds_AreIsolatedChangeSets()
        {
            var first = Guid.NewGuid();
            var second = Guid.NewGuid();

            _journal.Append(first, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.ProductTtlPolicies));
            _journal.Append(second, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.ProductTtlPolicies));

            Assert.Single(_journal.Get(first).Entries);
            Assert.Single(_journal.Get(second).Entries);
        }

        [Fact]
        public void Append_RolledBackSet_ForksFurtherWritesToAFreshId()
        {
            // A rolled-back set is terminal, but the write already applied —
            // rejecting after the fact would lose its undo point. The append
            // FORKS to a fresh id instead; the terminal set stays untouched.
            var id = Guid.NewGuid();

            _journal.Append(id, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.SensorDataPolicies));
            _journal.MarkRolledBack(id, Guid.NewGuid());

            var forked = _journal.Append(id, InitiatorInfo.AsUser("alice"),
                Entry(ChangeSetEntryKind.SensorDataPolicies));

            Assert.NotEqual(id, forked);
            Assert.Equal(ChangeSetStatus.RolledBack, _journal.Get(id).Status);
            Assert.Single(_journal.Get(forked).Entries);
        }

        [Fact]
        public void Append_StoragePersistsAcrossJournalRecreation()
        {
            // Simulated restart: a fresh journal instance over the SAME test
            // LevelDB sees the recorded change set (durability across
            // redeploy is a #1570 user story).
            var id = Guid.NewGuid();
            var policyId = Guid.NewGuid();

            _journal.Append(id, InitiatorInfo.AsUser("alice"),
                Entry(ChangeSetEntryKind.SensorDataPolicies,
                    before: [PolicyRow(policyId)], after: [PolicyRow(policyId), PolicyRow(Guid.NewGuid())]));

            var restarted = new ChangeSetJournal(_databaseCoreManager.DatabaseCore);
            var stored = restarted.Get(id);

            Assert.NotNull(stored);
            var entry = Assert.Single(stored.Entries);

            Assert.Equal(2, entry.After.Count);
            Assert.Equal(policyId, new Guid(entry.After[0].Id));
        }

        [Fact]
        public void GetAll_ReturnsNewestFirst()
        {
            var old = Guid.NewGuid();
            var newer = Guid.NewGuid();

            _journal.Append(old, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.SensorDataPolicies));
            Thread.Sleep(20);
            _journal.Append(newer, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.SensorDataPolicies));

            var all = _journal.GetAll();

            Assert.Equal(newer, all[0].Id);
            Assert.Equal(old, all[1].Id);
        }

        [Fact]
        public void MarkRolledBack_StampsEveryEntryAndStatus()
        {
            var id = Guid.NewGuid();
            var rollbackId = Guid.NewGuid();

            _journal.Append(id, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.SensorDataPolicies));
            _journal.Append(id, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.SensorTtlPolicies));

            _journal.MarkRolledBack(id, rollbackId);

            var stored = _journal.Get(id);

            Assert.Equal(ChangeSetStatus.RolledBack, stored.Status);
            Assert.All(stored.Entries, entry => Assert.Equal(rollbackId, entry.RestoredBy));
        }

        [Fact]
        public void MarkEntryRestored_RecordsTheRestoredByLink()
        {
            var id = Guid.NewGuid();
            var rollbackId = Guid.NewGuid();

            _journal.Append(id, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.SensorDataPolicies));
            _journal.Append(id, InitiatorInfo.AsUser("alice"), Entry(ChangeSetEntryKind.SensorTtlPolicies));

            _journal.MarkEntryRestored(id, 0, rollbackId);

            var stored = _journal.Get(id);

            Assert.Equal(rollbackId, stored.Entries[0].RestoredBy);
            Assert.Null(stored.Entries[1].RestoredBy);
            Assert.Equal(ChangeSetStatus.Applied, stored.Status);
        }

        [Fact]
        public void PurgeReferences_DropsEntriesOfDeletedSensor_MarksSetPartial()
        {
            var sensorId = Guid.NewGuid();
            var id = Guid.NewGuid();

            _journal.Append(id, InitiatorInfo.AsUser("alice"),
                Entry(ChangeSetEntryKind.SensorDataPolicies, entityId: sensorId));
            _journal.Append(id, InitiatorInfo.AsUser("alice"),
                Entry(ChangeSetEntryKind.SensorTtlPolicies, entityId: Guid.NewGuid()));

            var touched = _journal.PurgeReferences(sensorId);

            Assert.Equal(1, touched);

            var stored = _journal.Get(id);

            Assert.Equal(ChangeSetStatus.Partial, stored.Status);
            Assert.Single(stored.Entries);
        }

        [Fact]
        public void PurgeReferences_RemovesEntrylessChangeSet_Entirely()
        {
            var sensorId = Guid.NewGuid();
            var id = Guid.NewGuid();

            _journal.Append(id, InitiatorInfo.AsUser("alice"),
                Entry(ChangeSetEntryKind.SensorDataPolicies, entityId: sensorId));

            _journal.PurgeReferences(sensorId);

            Assert.Null(_journal.Get(id));
        }

        [Fact]
        public void PurgeReferences_DropsFanoutImageOfDeletedSensor_KeepsEntry()
        {
            var sensorId = Guid.NewGuid();
            var id = Guid.NewGuid();
            var entry = Entry(ChangeSetEntryKind.AlertTemplate, fanout:
            [
                new NodePolicyImage { NodeId = sensorId, Path = "P/S1" },
                new NodePolicyImage { NodeId = Guid.NewGuid(), Path = "P/S2" },
            ]);

            _journal.Append(id, InitiatorInfo.AsUser("alice"), entry);

            _journal.PurgeReferences(sensorId);

            var stored = _journal.Get(id);

            Assert.Single(stored.Entries);
            var image = Assert.Single(stored.Entries[0].Fanout);
            Assert.Equal("P/S2", image.Path);
            Assert.Equal(ChangeSetStatus.Partial, stored.Status);
        }

        [Fact]
        public void PurgeReferences_ByFolder_DropsFolderBoundTemplateEntries()
        {
            var folderId = Guid.NewGuid();
            var id = Guid.NewGuid();

            _journal.Append(id, InitiatorInfo.AsUser("alice"),
                Entry(ChangeSetEntryKind.AlertTemplate, entityId: Guid.NewGuid(), parentId: folderId));
            _journal.Append(id, InitiatorInfo.AsUser("alice"),
                Entry(ChangeSetEntryKind.SensorDataPolicies, entityId: Guid.NewGuid()));

            _journal.PurgeReferences(Guid.NewGuid(), folderId);

            var stored = _journal.Get(id);

            Assert.Single(stored.Entries);
            Assert.Equal(ChangeSetEntryKind.SensorDataPolicies, stored.Entries[0].Kind);
        }


        private static ChangeSetEntry Entry(ChangeSetEntryKind kind, Guid? entityId = null, Guid? parentId = null,
            List<PolicyEntity> before = null, List<PolicyEntity> after = null,
            List<NodePolicyImage> fanout = null) =>
            new()
            {
                Kind = kind,
                Operation = ChangeSetOperation.Update,
                EntityId = entityId ?? Guid.NewGuid(),
                ParentId = parentId,
                Path = "ProductA/sensor",
                Before = before,
                After = after,
                Fanout = fanout,
            };

        private static PolicyEntity PolicyRow(Guid id) => new() { Id = id.ToByteArray() };


        public sealed class ChangeSetJournalFixture : DatabaseFixture
        {
            protected override string DatabaseFolder => nameof(ChangeSetJournalTests);
        }
    }
}
