using System;
using System.Collections.Generic;
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
    public class NodeTTLFromParentResolutionTests
    {
        // 5 minutes in .NET ticks. TimeIntervalModel stores raw ticks for Ticks-kind intervals.
        private const long FiveMinutesTicks = 5 * TimeSpan.TicksPerMinute;

        // 10 minutes — distinct value used on a grandparent to prove the chain is NOT climbed.
        private const long TenMinutesTicks = 10 * TimeSpan.TicksPerMinute;


        [Fact]
        [Trait("Category", "Node TTL From Parent resolution")]
        public void NodeTTL_FromParent_ResolvesFromNodeOwnSettings()
        {
            // Build parent → child product so the child has a real parent chain that could be climbed.
            var parentProduct = new ProductModel("parent");
            var childProduct = new ProductModel("child");
            parentProduct.AddSubProduct(childProduct);

            // Grandparent has an explicit TTL — would be picked up if the chain were climbed.
            parentProduct.Settings.TTL.TrySetValue(new TimeIntervalModel(TenMinutesTicks));

            // The child product itself has an explicit "Time to sensors live" of 5 minutes.
            childProduct.Settings.TTL.TrySetValue(new TimeIntervalModel(FiveMinutesTicks));

            // Add a TTL alert on the child product with the period set to "From Parent" (TTL == null).
            childProduct.Policies.UpdateTTLs(
            [
                new PolicyUpdate
                {
                    Id = Guid.NewGuid(),
                    TTL = null,
                    Initiator = InitiatorInfo.AsUser("test"),
                    Conditions = [],
                    Destination = new PolicyDestinationUpdate(),
                },
            ], InitiatorInfo.AsUser("test"));

            var policy = Assert.Single(childProduct.Policies.TTLPolicies);

            Assert.True(policy.IsTTLFromParent, "policy should still report IsTTLFromParent (no concrete TTL stored)");
            Assert.Equal(FiveMinutesTicks, policy.TTLTicks);
            Assert.Equal(FiveMinutesTicks, policy.TTLInterval.Ticks);
        }

        [Fact]
        [Trait("Category", "Node TTL From Parent resolution")]
        public void NodeTTL_FromParent_WithNodeOwnSettingsAlsoFromParent_ReturnsNever()
        {
            var parentProduct = new ProductModel("parent");
            var childProduct = new ProductModel("child");
            parentProduct.AddSubProduct(childProduct);

            // Grandparent has an explicit TTL of 10 minutes. If resolution climbed the chain,
            // this is what the child's TTL policy would pick up.
            parentProduct.Settings.TTL.TrySetValue(new TimeIntervalModel(TenMinutesTicks));

            // The child product itself has NO explicit TTL — default is FromParent.
            // Do not call TrySetValue on childProduct.Settings.TTL — leave it as the default.

            childProduct.Policies.UpdateTTLs(
            [
                new PolicyUpdate
                {
                    Id = Guid.NewGuid(),
                    TTL = null,
                    Initiator = InitiatorInfo.AsUser("test"),
                    Conditions = [],
                    Destination = new PolicyDestinationUpdate(),
                },
            ], InitiatorInfo.AsUser("test"));

            var policy = Assert.Single(childProduct.Policies.TTLPolicies);

            Assert.True(policy.IsTTLFromParent);
            // Bounded resolution: with no explicit value on this node, the result is Never (None).
            Assert.True(policy.TTLInterval.IsNone);
            Assert.Equal(long.MaxValue, policy.TTLInterval.Ticks);
        }

        [Fact]
        [Trait("Category", "Node TTL From Parent resolution")]
        public void NodeTTL_FromParent_PropagatesToChildSensor()
        {
            // Mirrors TreeValuesCache.AddSensor: when a sensor is added under a product,
            // it inherits a child TTL policy for each of the product's TTLPolicies via ApplyParent.
            var product = new ProductModel("parent");
            product.Settings.TTL.TrySetValue(new TimeIntervalModel(FiveMinutesTicks));

            product.Policies.UpdateTTLs(
            [
                new PolicyUpdate
                {
                    Id = Guid.NewGuid(),
                    TTL = null,
                    Initiator = InitiatorInfo.AsUser("test"),
                    Conditions = [],
                    Destination = new PolicyDestinationUpdate(),
                },
            ], InitiatorInfo.AsUser("test"));

            var scheduleProvider = new Mock<IAlertScheduleProvider>();
            var sensorEntity = EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer);
            var sensor = new IntegerSensorModel(sensorEntity, null, scheduleProvider.Object);
            product.AddSensor(sensor);

            // Reproduce the inheritance step from TreeValuesCache.AddSensor (line ~2241).
            foreach (var parentTtl in product.Policies.TTLPolicies)
            {
                var childTtl = new TTLPolicy();
                childTtl.ApplyParent(parentTtl);
                sensor.Policies.AddTTLPolicy(childTtl);
            }

            var sensorTtl = Assert.Single(sensor.Policies.TTLPolicies);

            // The inherited policy must reflect the product's own TTL (5 minutes),
            // not climb further up the chain.
            Assert.Equal(FiveMinutesTicks, sensorTtl.TTLTicks);
        }

        [Fact]
        [Trait("Category", "Node TTL From Parent resolution")]
        public void SensorTTL_FromParent_StillClimbsParentChain()
        {
            // Regression test: sensor-level TTL alert with "From Parent" must keep resolving
            // via the sensor's parent chain (sensor → product → grandparent).
            var grandparent = new ProductModel("grandparent");
            grandparent.Settings.TTL.TrySetValue(new TimeIntervalModel(TenMinutesTicks));

            var parent = new ProductModel("parent");
            grandparent.AddSubProduct(parent);

            var scheduleProvider = new Mock<IAlertScheduleProvider>();
            var sensorEntity = EntitiesFactory.BuildSensorEntity(type: (byte)SensorType.Integer);
            var sensor = new IntegerSensorModel(sensorEntity, null, scheduleProvider.Object);
            parent.AddSensor(sensor);

            // Add a TTL alert on the sensor with "From Parent" (TTL == null).
            sensor.Policies.UpdateTTLs(
            [
                new PolicyUpdate
                {
                    Id = Guid.NewGuid(),
                    TTL = null,
                    Initiator = InitiatorInfo.AsUser("test"),
                    Conditions = [],
                    Destination = new PolicyDestinationUpdate(),
                },
            ], InitiatorInfo.AsUser("test"));

            var policy = Assert.Single(sensor.Policies.TTLPolicies);

            Assert.True(policy.IsTTLFromParent);
            // Sensor's parent chain: sensor.Settings.TTL → parent.Settings.TTL → grandparent.Settings.TTL (10 min).
            Assert.Equal(TenMinutesTicks, policy.TTLTicks);
        }
    }
}
