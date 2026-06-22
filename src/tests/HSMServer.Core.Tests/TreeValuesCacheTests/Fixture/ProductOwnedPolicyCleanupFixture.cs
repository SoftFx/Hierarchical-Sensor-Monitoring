using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using System;
using System.Collections.Generic;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    public sealed class ProductOwnedPolicyCleanupFixture : DatabaseFixture
    {
        protected override string DatabaseFolder => nameof(ProductOwnedPolicyCleanupTests);

        internal Guid UserAddedPolicyId { get; private set; }
        internal Guid TemplateDerivedPolicyId { get; private set; }
        internal Guid TemplateId { get; private set; }
        internal Guid ProductWithPoliciesId { get; private set; }
        internal Guid ProductWithTtlOnlyId { get; private set; }
        internal Guid SensorId { get; private set; }
        internal Guid SensorPolicyId { get; private set; }


        internal override void InitializeDatabase(IDatabaseCore dbCore)
        {
            UserAddedPolicyId = Guid.NewGuid();
            TemplateDerivedPolicyId = Guid.NewGuid();
            TemplateId = Guid.NewGuid();
            ProductWithPoliciesId = Guid.NewGuid();
            ProductWithTtlOnlyId = Guid.NewGuid();
            SensorId = Guid.NewGuid();
            SensorPolicyId = Guid.NewGuid();

            var userAddedPolicy = BuildPolicy(UserAddedPolicyId, templateId: null);
            var templateDerivedPolicy = BuildPolicy(TemplateDerivedPolicyId, templateId: TemplateId);
            var sensorPolicy = BuildPolicy(SensorPolicyId, templateId: null);

            dbCore.AddPolicy(userAddedPolicy);
            dbCore.AddPolicy(templateDerivedPolicy);
            dbCore.AddPolicy(sensorPolicy);

            var productWithPolicies = EntitiesFactory.BuildProductEntity("product_with_policies", null)
                with
            {
                Id = ProductWithPoliciesId.ToString(),
                Policies =
                [
                    UserAddedPolicyId.ToString(),
                    TemplateDerivedPolicyId.ToString(),
                ],
            };

            var ttlPolicy = BuildPolicy(Guid.NewGuid(), templateId: null, ttl: TimeSpan.FromMinutes(5).Ticks);
            dbCore.AddPolicy(ttlPolicy);

            var productWithTtlOnly = EntitiesFactory.BuildProductEntity("product_with_ttl_only", null)
                with
            {
                Id = ProductWithTtlOnlyId.ToString(),
                TTLPolicies = [ttlPolicy],
            };

            dbCore.AddProduct(productWithPolicies);
            dbCore.AddProduct(productWithTtlOnly);

            var sensor = new SensorEntity
            {
                Id = SensorId.ToString(),
                ProductId = ProductWithTtlOnlyId.ToString(),
                DisplayName = "sensor_with_policy",
                Type = (byte)SensorType.Boolean,
                Integration = (int)Integration.Grafana,
                Policies = [SensorPolicyId.ToString()],
            };
            dbCore.AddSensor(sensor);
        }

        private static PolicyEntity BuildPolicy(Guid id, Guid? templateId, long? ttl = null) =>
            new()
            {
                Id = id.ToByteArray(),
                TemplateId = templateId?.ToByteArray(),
                TTL = ttl,
                Conditions = [],
                Destination = new PolicyDestinationEntity { IsNotInitialized = true },
                Schedule = new PolicyScheduleEntity(),
            };
    }
}
