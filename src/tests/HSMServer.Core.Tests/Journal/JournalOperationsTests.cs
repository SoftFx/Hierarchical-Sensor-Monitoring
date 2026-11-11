using System;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.TableOfChanges;
using HSMDatabase.AccessManager.DatabaseEntities;
using Xunit;

namespace HSMServer.Core.Tests.Journal
{
    // #1638: the operation tag of a journal record — create / update /
    // delete / rollback — derived from the data the stored rows already
    // carry, without touching the storage contract. Rollback rows are named
    // by their enviroment marker (their property carries the INVERSE
    // compensating operation), templates and schedules by the operation part
    // of their property, policy rows by their undo-group entry, tree CRUD
    // and node settings rows by the legacy enviroment wording.
    public class JournalOperationsTests
    {
        [Theory]
        [InlineData("AlertTemplate:Create", "create")]
        [InlineData("AlertTemplate:Update", "update")]
        [InlineData("AlertSchedule:Delete", "delete")]
        [InlineData("SensorDataPolicies:Update", "update")]
        public void PropertyFormat_NamesTheOperation(string property, string expected)
        {
            Assert.Equal(expected, JournalOperations.Of(Record(property: property)));
        }

        [Theory]
        [InlineData("Alert:Remove", null)]   // operation part must be exact
        [InlineData("Alert", null)]
        [InlineData("AlertTemplate:", null)] // empty operation part
        [InlineData("General info", null)]
        [InlineData(null, null)]
        public void UnknownProperty_NamesNoOperation(string property, string expected)
        {
            Assert.Equal(expected, JournalOperations.Of(Record(property: property)));
        }

        [Fact]
        public void RollbackMarker_WinsOverTheInversePropertyOperation()
        {
            // A rollback of a CREATE journals as "…:Delete" (the compensating
            // write) — the enviroment marker is what names it a rollback.
            var record = Record(property: "AlertTemplate:Delete", enviroment: JournalOperations.RollbackEnviroment);

            Assert.Equal(JournalOperations.Rollback, JournalOperations.Of(record));
        }

        [Fact]
        public void GroupEntry_NamesPolicyOperations()
        {
            // Policy rows carry no "{Kind}:{Operation}" property — their
            // operation lives on the undo-group entry keyed by the entity.
            var entityId = Guid.NewGuid();
            var group = new ChangeSetModel
            {
                Entries =
                [
                    new ChangeSetEntry
                    {
                        Kind = ChangeSetEntryKind.SensorDataPolicies,
                        Operation = ChangeSetOperation.Create,
                        EntityId = entityId,
                    },
                ],
            };

            var record = Record(property: "Alert", entityId: entityId,
                changeSetId: Guid.NewGuid().ToString(), kind: JournalKinds.SensorDataPolicies);

            Assert.Equal(JournalOperations.Create, JournalOperations.Of(record, _ => group));
        }

        [Fact]
        public void GroupEntry_MatchesTheRecordKind_NotTheFirstEntryOfTheEntity()
        {
            // One group can journal a sensor's DATA and TTL lists as two
            // entries (and two records) — each record names ITS entry's
            // operation, not the first hit by entity id.
            var entityId = Guid.NewGuid();
            var group = new ChangeSetModel
            {
                Entries =
                [
                    new ChangeSetEntry
                    {
                        Kind = ChangeSetEntryKind.SensorDataPolicies,
                        Operation = ChangeSetOperation.Update,
                        EntityId = entityId,
                    },
                    new ChangeSetEntry
                    {
                        Kind = ChangeSetEntryKind.SensorTtlPolicies,
                        Operation = ChangeSetOperation.Delete,
                        EntityId = entityId,
                    },
                ],
            };

            var data = Record(property: "Alert", entityId: entityId,
                changeSetId: Guid.NewGuid().ToString(), kind: JournalKinds.SensorDataPolicies);
            var ttl = Record(property: "TTL policy", entityId: entityId,
                changeSetId: Guid.NewGuid().ToString(), kind: JournalKinds.SensorTtlPolicies);

            Assert.Equal(JournalOperations.Update, JournalOperations.Of(data, _ => group));
            Assert.Equal(JournalOperations.Delete, JournalOperations.Of(ttl, _ => group));
        }

        [Fact]
        public void GroupEntry_IsNotConsulted_WithoutAResolver()
        {
            // The per-node tab streams records without undo-group access —
            // a policy row with no derivable operation answers null, never
            // throws.
            var record = Record(property: "Alert", changeSetId: Guid.NewGuid().ToString(),
                kind: JournalKinds.SensorDataPolicies);

            Assert.Null(JournalOperations.Of(record));
        }

        [Fact]
        public void GroupResolver_IsConsultedOnlyForRollbackableKinds()
        {
            var record = Record(property: "product", changeSetId: Guid.NewGuid().ToString(),
                kind: JournalKinds.TreeCrud);

            Assert.Null(JournalOperations.Of(record, _ => throw new InvalidOperationException("must not resolve")));
        }

        [Theory]
        [InlineData("Create product", "create")]
        [InlineData("Remove sensor", "delete")]
        [InlineData("Remove node", "delete")]
        [InlineData("Move product", "update")]
        [InlineData("General info update", "update")]
        [InlineData("Settings update", "update")]
        [InlineData("Alert collection", null)]
        public void LegacyEnviroment_NamesTreeCrudAndSettingsOperations(string enviroment, string expected)
        {
            Assert.Equal(expected, JournalOperations.Of(Record(property: "product", enviroment: enviroment)));
        }

        [Fact]
        public void NullRecord_AnswersNull()
        {
            Assert.Null(JournalOperations.Of(null));
        }


        private static JournalRecordModel Record(string property = null, string enviroment = null,
            Guid? entityId = null, string changeSetId = null, string kind = null) =>
            new(entityId ?? Guid.NewGuid(), InitiatorInfo.AsUser("tester"))
            {
                PropertyName = property,
                Enviroment = enviroment,
                Kind = kind,
                ChangeSetId = changeSetId,
            };
    }
}
