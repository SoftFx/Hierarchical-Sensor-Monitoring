using System;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    [Collection("Database collection")]
    public class TemplateApplicationTests : MonitoringCoreTestsBase<TemplateApplicationFixture>
    {
        private readonly TemplateApplicationFixture _fixture;

        public TemplateApplicationTests(TemplateApplicationFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task NewSensor_MatchingTemplateWithTTL_GetsTemplateTTLPolicy()
        {
            // Arrange: create template with a TTL entry and add it.
            // Pattern must include the product-name prefix: FullPath is "TemplateTestProduct/testIntegerSensor"
            // and '*' matches only within a single path segment (PathTemplateConverter charset excludes '/').
            var template = BuildTemplate(SensorType.Integer, "*/testIntegerSensor",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (success, error) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(success, $"Failed to add template: {error}");

            // Act: add a sensor value that creates a new sensor
            var sensorPath = "testIntegerSensor";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var result = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);

            Assert.True(result.IsOk, $"Failed to add sensor value: {result.Error}");
            await Task.Delay(200);

            // Assert: sensor should have the template's TTL policy applied
            var found = _valuesCache.TryGetSensorByPath(
                _fixture.ProductId, sensorPath, out var sensor);
            Assert.True(found, "Sensor was not found in cache");
            Assert.NotNull(sensor);

            Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(template.Id, sensor.Policies.TTLPolicies[0].TemplateId);
        }

        [Fact]
        [Trait("Category", "Template application")]
        public async Task NewSensor_NotMatchingTemplate_GetsNoPolicies()
        {
            // Arrange: template path matches the sensor, but sensor type does not.
            // Path pattern must include the product-name prefix to actually reach the type check;
            // otherwise the test passes by accident (broken path also yields Empty).
            var template = BuildTemplate(SensorType.Double, "*/testIntegerSensor_noMatch",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (success, error) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(success, $"Failed to add template: {error}");

            // Act: add an Integer sensor (type mismatch)
            var sensorPath = "testIntegerSensor_noMatch";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var result = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(result.IsOk, $"Failed to add sensor value: {result.Error}");
            await Task.Delay(200);

            // Assert: no TTL policies applied
            var found = _valuesCache.TryGetSensorByPath(
                _fixture.ProductId, sensorPath, out var sensor);
            Assert.True(found, "Sensor was not found in cache");
            Assert.Empty(sensor.Policies.TTLPolicies);
        }

        [Fact]
        [Trait("Category", "Template application")]
        public async Task NewSensor_MatchingMultipleTemplates_GetsAllPolicies()
        {
            // Arrange: create two templates with different TTLs.
            // Both patterns must match the sensor's full product-prefixed path.
            var template1 = BuildTemplate(SensorType.Integer, "*/testIntegerSensor_multi",
                ttlInterval: TimeSpan.FromMinutes(5));
            var template2 = BuildTemplate(SensorType.Integer, "*/testIntegerSensor_multi",
                ttlInterval: TimeSpan.FromMinutes(10));

            var (s1, e1) = await _valuesCache.AddAlertTemplateAsync(template1);
            Assert.True(s1, $"Failed to add template 1: {e1}");
            var (s2, e2) = await _valuesCache.AddAlertTemplateAsync(template2);
            Assert.True(s2, $"Failed to add template 2: {e2}");

            // Act
            var sensorPath = "testIntegerSensor_multi";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var result = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(result.IsOk, $"Failed to add sensor value: {result.Error}");
            await Task.Delay(200);

            // Assert
            var found = _valuesCache.TryGetSensorByPath(
                _fixture.ProductId, sensorPath, out var sensor);
            Assert.True(found, "Sensor was not found in cache");

            // Positional order is not part of the contract: _alertTemplates is a
            // ConcurrentDictionary (insertion order undefined), and the per-sensor queue
            // applies matching templates in whatever order the dispatch loop sees them.
            // Even with the OrderBy on the dispatch loop, asserting position here would
            // couple the test to that implementation detail. Assert set membership instead.
            Assert.Equal(2, sensor.Policies.TTLPolicies.Count);
            var templateIds = sensor.Policies.TTLPolicies
                .Select(p => p.TemplateId!.Value)
                .OrderBy(id => id)
                .ToArray();
            var expectedIds = new[] { template1.Id, template2.Id }.OrderBy(id => id).ToArray();
            Assert.Equal(expectedIds, templateIds);
        }

        [Fact]
        [Trait("Category", "Template application")]
        public async Task NewSensor_MatchingPathWildcard_GetsPolicies()
        {
            // Arrange: template with specific path pattern.
            // FullPath is "TemplateTestProduct/group/server1/temperature", so the pattern must
            // consume the product-name segment with a leading '*' before matching the rest.
            var template = BuildTemplate(SensorType.Integer, "*/group/*/temperature",
                ttlInterval: TimeSpan.FromMinutes(3));

            var (success, error) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(success, $"Failed to add template: {error}");

            // Act: sensor path matches the pattern
            var sensorPath = "group/server1/temperature";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var result = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(result.IsOk, $"Failed to add sensor value: {result.Error}");
            await Task.Delay(200);

            // Assert
            var found = _valuesCache.TryGetSensorByPath(
                _fixture.ProductId, sensorPath, out var sensor);
            Assert.True(found, "Sensor was not found in cache");

            Assert.Single(sensor.Policies.TTLPolicies);
            Assert.Equal(template.Id, sensor.Policies.TTLPolicies[0].TemplateId);
        }

        [Fact]
        [Trait("Category", "Template application")]
        public async Task NewSensor_NonMatchingPath_GetsNoPolicies()
        {
            // Arrange: pattern requires group/.../temperature after the product segment.
            var template = BuildTemplate(SensorType.Integer, "*/group/*/temperature",
                ttlInterval: TimeSpan.FromMinutes(3));

            var (success, error) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(success, $"Failed to add template: {error}");

            // Act: sensor path does NOT match (disjoint segments under the product)
            var sensorPath = "other/humidity";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var result = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(result.IsOk, $"Failed to add sensor value: {result.Error}");
            await Task.Delay(200);

            // Assert
            var found = _valuesCache.TryGetSensorByPath(
                _fixture.ProductId, sensorPath, out var sensor);
            Assert.True(found, "Sensor was not found in cache");
            Assert.Empty(sensor.Policies.TTLPolicies);
        }


        private AlertTemplateModel BuildTemplate(SensorType sensorType, string pathPattern,
            TimeSpan ttlInterval)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(ttlInterval.Ticks));

            var template = new AlertTemplateModel
            {
                Name = $"Test template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = (byte)sensorType,
                Paths = [pathPattern],
                TtlEntries =
                [
                    new TtlEntry(new TTLPolicy(ttlSetting, null), ttlSetting.Value ?? TimeIntervalModel.None),
                ],
            };
            template.TryApplyPathTemplates(out _);

            return template;
        }
    }
}
