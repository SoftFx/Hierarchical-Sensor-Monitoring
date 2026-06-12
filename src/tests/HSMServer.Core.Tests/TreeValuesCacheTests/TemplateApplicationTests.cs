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
            // Arrange: create template with a TTL entry and add it
            var template = BuildTemplate(SensorType.Integer, "**",
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
            // Arrange: template matches only Double sensors
            var template = BuildTemplate(SensorType.Double, "**",
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
            // Arrange: create two templates with different TTLs
            var template1 = BuildTemplate(SensorType.Integer, "**",
                ttlInterval: TimeSpan.FromMinutes(5));
            var template2 = BuildTemplate(SensorType.Integer, "**",
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

            Assert.Equal(2, sensor.Policies.TTLPolicies.Count);
            Assert.Equal(template1.Id, sensor.Policies.TTLPolicies[0].TemplateId);
            Assert.Equal(template2.Id, sensor.Policies.TTLPolicies[1].TemplateId);
        }

        [Fact]
        [Trait("Category", "Template application")]
        public async Task NewSensor_MatchingPathWildcard_GetsPolicies()
        {
            // Arrange: template with specific path pattern
            var template = BuildTemplate(SensorType.Integer, "group/*/temperature",
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
            // Arrange
            var template = BuildTemplate(SensorType.Integer, "group/*/temperature",
                ttlInterval: TimeSpan.FromMinutes(3));

            var (success, error) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(success, $"Failed to add template: {error}");

            // Act: sensor path does NOT match
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
