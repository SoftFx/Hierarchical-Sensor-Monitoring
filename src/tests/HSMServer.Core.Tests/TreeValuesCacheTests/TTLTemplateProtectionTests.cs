using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public class TTLTemplateProtectionTests
    {
        private static readonly Guid TemplateA = Guid.NewGuid();
        private static readonly Guid TemplateB = Guid.NewGuid();

        private readonly Mock<IAlertScheduleProvider> _scheduleProvider = new();
        private readonly IntegerSensorModel _sensor;

        public TTLTemplateProtectionTests()
        {
            var sensorEntity = EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer);
            _sensor = new IntegerSensorModel(sensorEntity, null, _scheduleProvider.Object);
        }


        [Fact]
        [Trait("Category", "Template TTL protection")]
        public void UpdateTTLs_TemplateInitiated_PreservesManualTTLs()
        {
            var manualId = AddManualTTL(600_000_000); // 1 minute

            ApplyTemplateTTLs(TemplateA,
                (Guid.NewGuid(), 3_000_000_000, Guid.NewGuid()), // 5 minutes
                (Guid.NewGuid(), 6_000_000_000, Guid.NewGuid())); // 10 minutes

            Assert.Equal(3, _sensor.Policies.TTLPolicies.Count);
            Assert.Single(_sensor.Policies.TTLPolicies, p => p.Id == manualId);
        }

        [Fact]
        [Trait("Category", "Template TTL protection")]
        public void UpdateTTLs_TemplateInitiated_PreservesOtherTemplateTTLs()
        {
            var templateAAlertId = Guid.NewGuid();
            AddTemplateTTL(TemplateA, 3_000_000_000, templateAAlertId);

            ApplyTemplateTTLs(TemplateB,
                (Guid.NewGuid(), 6_000_000_000, Guid.NewGuid()),
                (Guid.NewGuid(), 12_000_000_000, Guid.NewGuid()));

            Assert.Equal(3, _sensor.Policies.TTLPolicies.Count);
            Assert.Single(_sensor.Policies.TTLPolicies, p => p.TemplateId == TemplateA);
            Assert.Equal(2, _sensor.Policies.TTLPolicies.Count(p => p.TemplateId == TemplateB));
        }

        [Fact]
        [Trait("Category", "Template TTL protection")]
        public void UpdateTTLs_ReapplyTemplate_UpdatesOnlyOwnTTLs()
        {
            var manualId = AddManualTTL(600_000_000);

            var alertId1 = Guid.NewGuid();
            var alertId2 = Guid.NewGuid();
            var ttl1Id = AddTemplateTTL(TemplateA, 3_000_000_000, alertId1);
            var ttl2Id = AddTemplateTTL(TemplateA, 6_000_000_000, alertId2);

            // Reapply same template with updated TTL values
            ApplyTemplateTTLs(TemplateA,
                (ttl1Id, 4_500_000_000, alertId1),
                (ttl2Id, 9_000_000_000, alertId2));

            Assert.Equal(3, _sensor.Policies.TTLPolicies.Count);

            var manual = _sensor.Policies.TTLPolicies.First(p => p.Id == manualId);
            Assert.Equal(600_000_000, manual.TTLTicks);

            var updated1 = _sensor.Policies.TTLPolicies.First(p => p.Id == ttl1Id);
            Assert.Equal(4_500_000_000, updated1.TTLTicks);

            var updated2 = _sensor.Policies.TTLPolicies.First(p => p.Id == ttl2Id);
            Assert.Equal(9_000_000_000, updated2.TTLTicks);
        }

        [Fact]
        [Trait("Category", "Template TTL protection")]
        public void UpdateTTLs_UserInitiated_RemovesManualTTLsNotInUpdate()
        {
            var keepId = AddManualTTL(600_000_000);
            AddManualTTL(3_000_000_000);

            _sensor.Policies.UpdateTTLs(
            [
                new PolicyUpdate
                {
                    Id = keepId,
                    TTL = 600_000_000,
                    Initiator = InitiatorInfo.AsUser("test"),
                    Conditions = [],
                    Destination = new PolicyDestinationUpdate(),
                },
            ]);

            Assert.Single(_sensor.Policies.TTLPolicies);
            Assert.Equal(keepId, _sensor.Policies.TTLPolicies[0].Id);
        }

        [Fact]
        [Trait("Category", "Template TTL protection")]
        public void UpdateTTLs_EmptySensor_TemplateAddsAll()
        {
            ApplyTemplateTTLs(TemplateA,
                (Guid.NewGuid(), 3_000_000_000, Guid.NewGuid()),
                (Guid.NewGuid(), 6_000_000_000, Guid.NewGuid()));

            Assert.Equal(2, _sensor.Policies.TTLPolicies.Count);
            Assert.All(_sensor.Policies.TTLPolicies, p => Assert.Equal(TemplateA, p.TemplateId));
        }

        [Fact]
        [Trait("Category", "Template TTL protection")]
        public void UpdateTTLs_TemplateWithNoTTLEntries_PreservesManualTTLs()
        {
            var manualId = AddManualTTL(600_000_000);

            // Simulates applying a template that has regular policies but no TTL entries:
            // ApplyTemplateToSensor sends SensorUpdate.TTLPolicies = [] (empty list),
            // which reaches UpdateTTLs with an empty list.
            _sensor.Policies.UpdateTTLs([]);

            Assert.Single(_sensor.Policies.TTLPolicies);
            Assert.Equal(manualId, _sensor.Policies.TTLPolicies[0].Id);
        }

        [Fact]
        [Trait("Category", "Template TTL protection")]
        public void UpdateTTLs_TemplatePreservesManualAndOtherTemplate()
        {
            var manualId = AddManualTTL(600_000_000);
            var templateAAlertId = Guid.NewGuid();
            AddTemplateTTL(TemplateA, 3_000_000_000, templateAAlertId);

            ApplyTemplateTTLs(TemplateB,
                (Guid.NewGuid(), 6_000_000_000, Guid.NewGuid()),
                (Guid.NewGuid(), 12_000_000_000, Guid.NewGuid()));

            Assert.Equal(4, _sensor.Policies.TTLPolicies.Count);
            Assert.Single(_sensor.Policies.TTLPolicies, p => p.Id == manualId && p.TemplateId == null);
            Assert.Single(_sensor.Policies.TTLPolicies, p => p.TemplateId == TemplateA);
            Assert.Equal(2, _sensor.Policies.TTLPolicies.Count(p => p.TemplateId == TemplateB));
        }


        // === Helpers ===

        private Guid AddManualTTL(long ttlTicks)
        {
            var id = Guid.NewGuid();
            _sensor.Policies.UpdateTTLs(
            [
                new PolicyUpdate
                {
                    Id = id,
                    TTL = ttlTicks,
                    Initiator = InitiatorInfo.AsUser("test"),
                    Conditions = [],
                    Destination = new PolicyDestinationUpdate(),
                },
            ]);
            return id;
        }

        private Guid AddTemplateTTL(Guid templateId, long ttlTicks, Guid templateAlertId)
        {
            var id = Guid.NewGuid();
            _sensor.Policies.UpdateTTLs(
            [
                new PolicyUpdate
                {
                    Id = id,
                    TTL = ttlTicks,
                    TemplateId = templateId,
                    TemplateAlertId = templateAlertId,
                    Initiator = InitiatorInfo.AlertTemplate,
                    Conditions = [],
                    Destination = new PolicyDestinationUpdate(),
                },
            ]);
            return id;
        }

        private void ApplyTemplateTTLs(Guid templateId, params (Guid policyId, long ttlTicks, Guid templateAlertId)[] entries)
        {
            var updates = entries.Select(e => new PolicyUpdate
            {
                Id = e.policyId,
                TTL = e.ttlTicks,
                TemplateId = templateId,
                TemplateAlertId = e.templateAlertId,
                Initiator = InitiatorInfo.AlertTemplate,
                Conditions = [],
                Destination = new PolicyDestinationUpdate(),
            }).ToList();

            _sensor.Policies.UpdateTTLs(updates);
        }
    }
}
