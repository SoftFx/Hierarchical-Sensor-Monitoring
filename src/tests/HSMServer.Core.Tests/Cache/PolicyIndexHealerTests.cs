using System;
using System.Collections.Generic;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using Xunit;

namespace HSMServer.Core.Tests.Cache
{
    // The boot path's self-heal over the policy-id index (#1407): rows whose
    // index entry was lost to the (now locked) concurrent-index race are
    // recovered through the sensor entities' own references. Pinned at the
    // healer unit level — the database-level race and direct-fetch seams live
    // in HSMDatabase.LevelDB.Tests (PolicyIdsIndexRaceTests).
    public class PolicyIndexHealerTests
    {
        [Fact]
        public void ReferencedIdMissingFromIndex_RowExists_IsHealed()
        {
            // The index-driven dictionary has the OTHER policy, not the orphan.
            var indexedRow = BuildRow();
            var orphanRow = BuildRow();
            var sensor = BuildSensor(new Guid(indexedRow.Id).ToString(), new Guid(orphanRow.Id).ToString());
            var policies = new Dictionary<string, PolicyEntity>
            {
                [new Guid(indexedRow.Id).ToString()] = indexedRow,
            };

            var (healed, unresolved) = PolicyIndexHealer.Heal(
                [sensor], policies, id => id == new Guid(orphanRow.Id) ? orphanRow : null);

            Assert.Equal(1, healed);
            Assert.Null(unresolved);
            Assert.True(policies.ContainsKey(new Guid(orphanRow.Id).ToString()));
            Assert.Same(orphanRow, policies[new Guid(orphanRow.Id).ToString()]);
        }


        [Fact]
        public void ReferencedId_RowMissingEverywhere_IsUnresolved()
        {
            var deadId = Guid.NewGuid();
            var sensor = BuildSensor(deadId.ToString());
            var policies = new Dictionary<string, PolicyEntity>();

            var (healed, unresolved) = PolicyIndexHealer.Heal(
                [sensor], policies, _ => null);

            Assert.Equal(0, healed);
            Assert.Equal([deadId], unresolved);
            Assert.Empty(policies);
        }


        [Fact]
        public void ReferencedIdAlreadyInDictionary_NotTouched()
        {
            var row = BuildRow();
            var sensor = BuildSensor(new Guid(row.Id).ToString());
            var policies = new Dictionary<string, PolicyEntity> { [new Guid(row.Id).ToString()] = row };
            var fetched = false;

            var (healed, unresolved) = PolicyIndexHealer.Heal(
                [sensor], policies, _ => { fetched = true; return row; });

            Assert.Equal(0, healed);
            Assert.Null(unresolved);
            Assert.False(fetched);
            Assert.Single(policies);
        }


        [Fact]
        public void DuplicateReferences_HealOnce()
        {
            // Two sensors referencing the same orphaned id: one heal, one
            // dictionary entry — and the second pass must not even fetch.
            var orphanRow = BuildRow();
            var sensorA = BuildSensor(new Guid(orphanRow.Id).ToString());
            var sensorB = BuildSensor(new Guid(orphanRow.Id).ToString());
            var policies = new Dictionary<string, PolicyEntity>();
            var fetches = 0;

            var (healed, unresolved) = PolicyIndexHealer.Heal(
                [sensorA, sensorB], policies,
                id =>
                {
                    fetches++;
                    return id == new Guid(orphanRow.Id) ? orphanRow : null;
                });

            Assert.Equal(1, healed);
            Assert.Null(unresolved);
            Assert.Equal(1, fetches);
            Assert.Single(policies);
        }


        [Fact]
        public void UnparseableReference_IsSkippedSilently()
        {
            // Pre-existing corruption, not this heal's concern: the reference
            // is neither healed nor unresolved — left exactly as the load
            // would have seen it.
            var sensor = BuildSensor("not-a-guid");
            var policies = new Dictionary<string, PolicyEntity>();
            var fetched = false;

            var (healed, unresolved) = PolicyIndexHealer.Heal(
                [sensor], policies, _ => { fetched = true; return null; });

            Assert.Equal(0, healed);
            Assert.Null(unresolved);
            Assert.False(fetched);
        }


        [Fact]
        public void NullEntityAndNullReferences_AreTolerated()
        {
            var (healed, unresolved) = PolicyIndexHealer.Heal(null, new Dictionary<string, PolicyEntity>(), _ => null);

            Assert.Equal(0, healed);
            Assert.Null(unresolved);

            (healed, unresolved) = PolicyIndexHealer.Heal([BuildSensor(null)], new Dictionary<string, PolicyEntity>(), _ => null);

            Assert.Equal(0, healed);
            Assert.Null(unresolved);
        }


        private static PolicyEntity BuildRow(Guid? id = null) => new()
        {
            Id = (id ?? Guid.NewGuid()).ToByteArray(),
            Template = "t",
            Conditions = [],
            Destination = new PolicyDestinationEntity { IsNotInitialized = true },
            Schedule = new PolicyScheduleEntity(),
        };

        private static SensorEntity BuildSensor(params string[] policyIds) => new()
        {
            Id = Guid.NewGuid().ToString(),
            ProductId = Guid.NewGuid().ToString(),
            Policies = policyIds is null ? null : [.. policyIds],
        };
    }
}
