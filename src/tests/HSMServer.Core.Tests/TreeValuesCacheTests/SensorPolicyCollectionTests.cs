using System;
using System.Collections.Generic;
using System.Linq;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
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
    public class SensorPolicyCollectionTests
    {
        private static readonly Guid TemplateId = Guid.NewGuid();
        private static readonly Guid TemplateAlertId = Guid.NewGuid();

        private readonly Mock<IAlertScheduleProvider> _scheduleProvider = new();
        private readonly SensorPolicyCollection<IntegerValue, IntegerPolicy> _collection;
        private readonly IntegerSensorModel _sensor;

        public SensorPolicyCollectionTests()
        {
            _collection = new SensorPolicyCollection<IntegerValue, IntegerPolicy>(_scheduleProvider.Object);

            var sensorEntity = EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer);
            _sensor = new IntegerSensorModel(sensorEntity, null, _scheduleProvider.Object);
            _collection.Attach(_sensor);
        }


        // === Template protection: user cannot modify template-created policies ===

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_UserCannotModifyTemplatePolicyProperties()
        {
            var policyId = AddTemplatePolicy();

            var update = BuildUpdate(policyId, templateId: TemplateId, template: "modified template");
            _collection.TryUpdate([update], InitiatorInfo.AsUser("test"), out _);

            var policy = _collection.First(p => p.Id == policyId);
            Assert.Equal("original template", policy.Template);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_UserCannotModifyTemplatePolicyIcon()
        {
            var policyId = AddTemplatePolicy();

            var update = BuildUpdate(policyId, templateId: TemplateId, icon: "new-icon");
            _collection.TryUpdate([update], InitiatorInfo.AsUser("test"), out _);

            var policy = _collection.First(p => p.Id == policyId);
            Assert.Equal("original-icon", policy.Icon);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_UserCannotModifyTemplatePolicyStatus()
        {
            var policyId = AddTemplatePolicy();

            var update = BuildUpdate(policyId, templateId: TemplateId, status: SensorStatus.Error);
            _collection.TryUpdate([update], InitiatorInfo.AsUser("test"), out _);

            var policy = _collection.First(p => p.Id == policyId);
            Assert.Equal(SensorStatus.Ok, policy.Status);
        }


        // === Template protection: user cannot delete template-created policies ===

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_UserCannotDeleteTemplatePolicy()
        {
            var policyId = AddTemplatePolicy();

            // Send empty update list — should not remove template policy
            _collection.TryUpdate([], InitiatorInfo.AsUser("test"), out _);

            Assert.Single(_collection);
            Assert.Equal(policyId, _collection.First().Id);
        }


        // === Template protection: user can toggle IsDisabled ===

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_UserCanDisableTemplatePolicy()
        {
            var policyId = AddTemplatePolicy(isDisabled: false);

            var update = BuildUpdate(policyId, templateId: TemplateId, isDisabled: true);
            _collection.TryUpdate([update], InitiatorInfo.AsUser("test"), out _);

            var policy = _collection.First(p => p.Id == policyId);
            Assert.True(policy.IsDisabled);
            // Template/Icon should remain unchanged
            Assert.Equal("original template", policy.Template);
            Assert.Equal("original-icon", policy.Icon);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_UserCanEnableTemplatePolicy()
        {
            var policyId = AddTemplatePolicy(isDisabled: true);

            var update = BuildUpdate(policyId, templateId: TemplateId, isDisabled: false);
            _collection.TryUpdate([update], InitiatorInfo.AsUser("test"), out _);

            var policy = _collection.First(p => p.Id == policyId);
            Assert.False(policy.IsDisabled);
        }


        // === AlertTemplate initiator: update-in-place ===

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_AlertTemplateUpdatesExistingPolicyInPlace()
        {
            var policyId = AddTemplatePolicy();

            var update = BuildUpdate(policyId, templateId: TemplateId, template: "updated template",
                templateAlertId: TemplateAlertId, isDisabled: false);
            _collection.TryUpdate([update], InitiatorInfo.AlertTemplate, out _);

            Assert.Single(_collection);
            var policy = _collection.First();
            Assert.Equal(policyId, policy.Id);
            Assert.Equal("updated template", policy.Template);
            Assert.Equal(TemplateId, policy.TemplateId);
            Assert.Equal(TemplateAlertId, policy.TemplateAlertId);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_AlertTemplatePreservesIsDisabledOnUpdate()
        {
            var policyId = AddTemplatePolicy(isDisabled: true);

            var update = BuildUpdate(policyId, templateId: TemplateId,
                templateAlertId: TemplateAlertId, isDisabled: true);
            _collection.TryUpdate([update], InitiatorInfo.AlertTemplate, out _);

            var policy = _collection.First();
            Assert.True(policy.IsDisabled);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_AlertTemplateAddsNewPolicy()
        {
            var newId = Guid.NewGuid();

            var update = BuildUpdate(newId, templateId: TemplateId,
                templateAlertId: TemplateAlertId);
            _collection.TryUpdate([update], InitiatorInfo.AlertTemplate, out _);

            Assert.Single(_collection);
            var policy = _collection.First();
            Assert.Equal(newId, policy.Id);
            Assert.Equal(TemplateId, policy.TemplateId);
            Assert.Equal(TemplateAlertId, policy.TemplateAlertId);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_AlertTemplateCanUpdateMultiplePolicies()
        {
            var policy1Id = AddTemplatePolicy();
            var policy2Id = AddTemplatePolicy();

            var update1 = BuildUpdate(policy1Id, templateId: TemplateId,
                templateAlertId: Guid.NewGuid(), template: "updated1");
            var update2 = BuildUpdate(policy2Id, templateId: TemplateId,
                templateAlertId: Guid.NewGuid(), template: "updated2");

            _collection.TryUpdate([update1, update2], InitiatorInfo.AlertTemplate, out _);

            var policies = _collection.ToList();
            Assert.Equal(2, policies.Count);
            Assert.Contains(policies, p => p.Id == policy1Id && p.Template == "updated1");
            Assert.Contains(policies, p => p.Id == policy2Id && p.Template == "updated2");
        }


        // === Manual policies: unaffected by template protection ===

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_UserCanFullyModifyManualPolicy()
        {
            var policyId = AddManualPolicy();

            var update = BuildUpdate(policyId, template: "modified", icon: "new-icon");
            _collection.TryUpdate([update], InitiatorInfo.AsUser("test"), out _);

            var policy = _collection.First(p => p.Id == policyId);
            Assert.Equal("modified", policy.Template);
            Assert.Equal("new-icon", policy.Icon);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_UserCanDeleteManualPolicy()
        {
            var policyId = AddManualPolicy();

            // Empty update list should remove manual policy
            _collection.TryUpdate([], InitiatorInfo.AsUser("test"), out _);

            Assert.Empty(_collection);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_UserCanDeleteManualPolicy_WhileTemplatePolicyPreserved()
        {
            var templatePolicyId = AddTemplatePolicy();
            var manualPolicyId = AddManualPolicy();

            // Send only the template policy update — manual one should be removed
            var update = BuildUpdate(templatePolicyId, templateId: TemplateId, isDisabled: false);
            _collection.TryUpdate([update], InitiatorInfo.AsUser("test"), out _);

            Assert.Single(_collection);
            Assert.Equal(templatePolicyId, _collection.First().Id);
        }


        // === TemplateAlertId propagation ===

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_AlertTemplateSetsTemplateAlertIdOnAdd()
        {
            var newId = Guid.NewGuid();
            var alertId = Guid.NewGuid();

            var update = BuildUpdate(newId, templateId: TemplateId, templateAlertId: alertId);
            _collection.TryUpdate([update], InitiatorInfo.AlertTemplate, out _);

            var policy = _collection.First();
            Assert.Equal(alertId, policy.TemplateAlertId);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_AlertTemplateSetsTemplateAlertIdOnUpdate()
        {
            // Add a template policy without TemplateAlertId (simulating legacy data)
            var policyId = AddTemplatePolicy(templateAlertId: null);

            var alertId = Guid.NewGuid();
            var update = BuildUpdate(policyId, templateId: TemplateId,
                templateAlertId: alertId, template: "updated");
            _collection.TryUpdate([update], InitiatorInfo.AlertTemplate, out _);

            var policy = _collection.First();
            Assert.Equal(alertId, policy.TemplateAlertId);
            Assert.Equal("updated", policy.Template);
        }


        // === Force update bypasses template protection ===

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_ForceUpdateCanModifyTemplatePolicy()
        {
            var policyId = AddTemplatePolicy();

            var update = BuildUpdate(policyId, templateId: TemplateId, template: "force modified");
            _collection.TryUpdate([update], InitiatorInfo.AsSystemForce("test"), out _);

            var policy = _collection.First(p => p.Id == policyId);
            Assert.Equal("force modified", policy.Template);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void TryUpdate_ForceUpdateCanDeleteTemplatePolicy()
        {
            var policyId = AddTemplatePolicy();

            // Empty update with force initiator
            _collection.TryUpdate([], InitiatorInfo.AsSystemForce("test"), out _);

            Assert.Empty(_collection);
        }


        // === Policy serialization round-trip ===

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void Policy_TemplateAlertId_SerializedInToEntity()
        {
            var policy = new IntegerPolicy();
            var update = new PolicyUpdate
            {
                Id = Guid.NewGuid(),
                Template = "test",
                TemplateId = TemplateId,
                TemplateAlertId = TemplateAlertId,
                Destination = new PolicyDestinationUpdate(),
                Conditions = [],
            };
            policy.TryUpdate(update, out _, null);

            var entity = policy.ToEntity();

            Assert.Equal(TemplateAlertId, new Guid(entity.TemplateAlertId));
            Assert.Equal(TemplateId, new Guid(entity.TemplateId));
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void Policy_TemplateAlertId_DeserializedInApply()
        {
            var entity = new PolicyEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Template = "test",
                TemplateId = TemplateId.ToByteArray(),
                TemplateAlertId = TemplateAlertId.ToByteArray(),
                Destination = new PolicyDestinationEntity { UseDefaultChats = true },
                Schedule = new PolicyScheduleEntity(),
                Conditions = [],
            };

            var policy = new IntegerPolicy();
            policy.Apply(entity);

            Assert.Equal(TemplateId, policy.TemplateId);
            Assert.Equal(TemplateAlertId, policy.TemplateAlertId);
        }

        [Fact]
        [Trait("Category", "Template alert protection")]
        public void Policy_TemplateAlertId_NullWhenEmptyInEntity()
        {
            var entity = new PolicyEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Template = "test",
                TemplateId = TemplateId.ToByteArray(),
                TemplateAlertId = [],
                Destination = new PolicyDestinationEntity { UseDefaultChats = true },
                Schedule = new PolicyScheduleEntity(),
                Conditions = [],
            };

            var policy = new IntegerPolicy();
            policy.Apply(entity);

            Assert.Null(policy.TemplateAlertId);
        }


        // === Helper methods ===

        private Guid AddTemplatePolicy(Guid? templateId = null, bool isDisabled = false,
            Guid? templateAlertId = null)
        {
            var policyId = Guid.NewGuid();
            var update = BuildUpdate(policyId,
                templateId: templateId ?? TemplateId,
                template: "original template",
                icon: "original-icon",
                isDisabled: isDisabled,
                templateAlertId: templateAlertId);

            _collection.TryUpdate([update], InitiatorInfo.AlertTemplate, out _);
            return policyId;
        }

        private Guid AddManualPolicy()
        {
            var policyId = Guid.NewGuid();
            var update = BuildUpdate(policyId, template: "manual policy", icon: "manual-icon");

            // Use AlertTemplate initiator to add, then User can modify (higher priority)
            _collection.TryUpdate([update], InitiatorInfo.AlertTemplate, out _);
            return policyId;
        }

        private static PolicyUpdate BuildUpdate(
            Guid id,
            Guid? templateId = null,
            string template = "test",
            string icon = null,
            bool isDisabled = false,
            SensorStatus status = SensorStatus.Ok,
            Guid? templateAlertId = null)
        {
            return new PolicyUpdate
            {
                Id = id,
                TemplateId = templateId,
                TemplateAlertId = templateAlertId,
                Template = template,
                Icon = icon,
                IsDisabled = isDisabled,
                Status = status,
                Destination = new PolicyDestinationUpdate(),
                Conditions =
                [
                    new PolicyConditionUpdate(
                        PolicyOperation.Equal,
                        PolicyProperty.Value,
                        new TargetValue(TargetType.Const, "1"))
                ],
            };
        }
    }
}
