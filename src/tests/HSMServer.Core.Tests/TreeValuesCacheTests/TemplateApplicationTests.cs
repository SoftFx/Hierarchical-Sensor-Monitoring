using System;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.NodeSettings;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
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


        // Regression for #1209: editing an Alert Template's sensor type so a previously matched
        // sensor no longer matches must prune the template-derived TTL policy from that sensor.
        // The save path goes through AddAlertTemplateAsync for both new and edited templates.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_ChangeSensorType_RemovesTtlFromNoLongerMatchingSensor()
        {
            // Arrange: create a template matching Integer sensors on */sensorEditType.
            var template = BuildTemplate(SensorType.Integer, "*/sensorEditType",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorEditType";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorBefore));
            Assert.Single(sensorBefore.Policies.TTLPolicies);
            Assert.Equal(template.Id, sensorBefore.Policies.TTLPolicies[0].TemplateId);

            // Act: re-issue AddAlertTemplateAsync with the same Id but a different SensorType,
            // so the existing sensor no longer matches the template.
            template.SensorType = (byte)SensorType.Double;
            template.TryApplyPathTemplates(out _);

            var (editOk, editError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");
            await Task.Delay(300);

            // Assert: the template TTL is gone from the sensor.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            Assert.DoesNotContain(sensorAfter.Policies.TTLPolicies, p => p.TemplateId == template.Id);
        }

        // Regression for #1209: editing an Alert Template's path so a previously matched sensor
        // no longer matches must prune the template-derived TTL policy from that sensor.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_ChangePath_RemovesTtlFromNoLongerMatchingSensor()
        {
            // Arrange: template matches */sensorEditPath.
            var template = BuildTemplate(SensorType.Integer, "*/sensorEditPath",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorEditPath";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorBefore));
            Assert.Single(sensorBefore.Policies.TTLPolicies);
            Assert.Equal(template.Id, sensorBefore.Policies.TTLPolicies[0].TemplateId);

            // Act: re-issue AddAlertTemplateAsync with the same Id but a path that no longer
            // matches the existing sensor.
            template.Paths = ["*/sensorDifferentPath"];
            template.TryApplyPathTemplates(out _);

            var (editOk, editError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");
            await Task.Delay(300);

            // Assert: the template TTL is gone from the sensor.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            Assert.DoesNotContain(sensorAfter.Policies.TTLPolicies, p => p.TemplateId == template.Id);
        }

        // Regression for #1209: when pruning stale template policies after an edit, a manual TTL
        // policy on the same sensor must remain untouched.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_ChangeSensorType_PreservesManualTtl()
        {
            // Arrange: create a matching template and sensor, then add a manual TTL.
            var template = BuildTemplate(SensorType.Integer, "*/sensorEditManual",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorEditManual";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            await AddManualTtlToSensor(_fixture.ProductId, sensorPath, TimeSpan.FromMinutes(10));

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorBefore));
            Assert.Equal(2, sensorBefore.Policies.TTLPolicies.Count);
            Assert.Contains(sensorBefore.Policies.TTLPolicies, p => p.TemplateId == template.Id);
            Assert.Contains(sensorBefore.Policies.TTLPolicies, p => p.TemplateId == null);

            // Act: edit the template so the sensor no longer matches.
            template.SensorType = (byte)SensorType.Double;
            template.TryApplyPathTemplates(out _);

            var (editOk, editError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");
            await Task.Delay(300);

            // Assert: only the template TTL is removed; the manual TTL remains.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var remaining = sensorAfter.Policies.TTLPolicies;
            Assert.Single(remaining);
            Assert.Null(remaining[0].TemplateId);
        }

        // Regression for #1209: when pruning stale template policies after an edit, policies from
        // a different template that still matches the sensor must remain untouched.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_ChangeSensorType_PreservesOtherTemplatePolicies()
        {
            // Arrange: two templates matching the same sensor; edit one away.
            var templateA = BuildTemplate(SensorType.Integer, "*/sensorEditOther",
                ttlInterval: TimeSpan.FromMinutes(5));
            var templateB = BuildTemplate(SensorType.Integer, "*/sensorEditOther",
                ttlInterval: TimeSpan.FromMinutes(10));

            var (addA, errA) = await _valuesCache.AddAlertTemplateAsync(templateA);
            Assert.True(addA, $"Failed to add template A: {errA}");
            var (addB, errB) = await _valuesCache.AddAlertTemplateAsync(templateB);
            Assert.True(addB, $"Failed to add template B: {errB}");

            var sensorPath = "sensorEditOther";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorBefore));
            Assert.Equal(2, sensorBefore.Policies.TTLPolicies.Count);
            var templateIds = sensorBefore.Policies.TTLPolicies
                .Select(p => p.TemplateId!.Value)
                .OrderBy(id => id)
                .ToArray();
            var expectedIds = new[] { templateA.Id, templateB.Id }.OrderBy(id => id).ToArray();
            Assert.Equal(expectedIds, templateIds);

            // Act: edit templateA so the sensor no longer matches it.
            templateA.SensorType = (byte)SensorType.Double;
            templateA.TryApplyPathTemplates(out _);

            var (editOk, editError) = await _valuesCache.AddAlertTemplateAsync(templateA);
            Assert.True(editOk, $"Failed to edit template A: {editError}");
            await Task.Delay(300);

            // Assert: only templateA's TTL is removed; templateB's TTL remains.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var remaining = sensorAfter.Policies.TTLPolicies;
            Assert.Single(remaining);
            Assert.Equal(templateB.Id, remaining[0].TemplateId);
        }

        // Regression for #1209: editing an Alert Template's folder moves it to a different folder.
        // Sensors in the OLD folder that previously matched must have the template-derived TTL
        // policy pruned — they can no longer match a template that now belongs to another folder.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_ChangeFolder_RemovesTtlFromSensorsInOldFolder()
        {
            // Arrange: template in Folder1 matching */sensorFolderChange.
            var template = BuildTemplate(SensorType.Integer, "*/sensorFolderChange",
                ttlInterval: TimeSpan.FromMinutes(5));
            template.FolderId = _fixture.FolderId;

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorFolderChange";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorBefore));
            Assert.Single(sensorBefore.Policies.TTLPolicies);
            Assert.Equal(template.Id, sensorBefore.Policies.TTLPolicies[0].TemplateId);

            // Act: re-issue AddAlertTemplateAsync with the same Id but FolderId moved to Folder2.
            // Use a fresh instance (mirrors the controller's data.ToModel path) so the cached
            // template still reflects the previous FolderId when AddAlertTemplateAsync captures it.
            var edited = new AlertTemplateModel
            {
                Id = template.Id,
                Name = template.Name,
                FolderId = _fixture.FolderId2,
                SensorType = template.SensorType,
                Paths = [.. template.Paths],
                TtlEntries = [.. template.TtlEntries],
            };
            edited.TryApplyPathTemplates(out _);

            var (editOk, editError) = await _valuesCache.AddAlertTemplateAsync(edited);
            Assert.True(editOk, $"Failed to edit template: {editError}");
            await Task.Delay(300);

            // Assert: the sensor in the old folder no longer carries the template TTL.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            Assert.DoesNotContain(sensorAfter.Policies.TTLPolicies, p => p.TemplateId == template.Id);
        }

        // Regression for #1209: regular (non-TTL) template policies are pruned by the same
        // reconciliation path. Verifies both policy kinds listed in the issue tasks.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_ChangeSensorType_RemovesRegularPolicyFromNoLongerMatchingSensor()
        {
            // Arrange: template with a regular Integer policy (value > 0), no TTL.
            var template = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorRegularPolicy");
            template.FolderId = _fixture.FolderId;

            var (addOk, addError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorRegularPolicy";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorBefore));
            Assert.Contains(sensorBefore.Policies, p => p.TemplateId == template.Id);

            // Act: change the template's SensorType so the Integer sensor no longer matches.
            template.SensorType = (byte)SensorType.Double;
            template.TryApplyPathTemplates(out _);

            var (editOk, editError) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");
            await Task.Delay(300);

            // Assert: the regular template policy is gone from the sensor.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            Assert.DoesNotContain(sensorAfter.Policies, p => p.TemplateId == template.Id);
        }


        private async Task AddManualTtlToSensor(Guid productId, string sensorPath, TimeSpan ttl)
        {
            Assert.True(_valuesCache.TryGetSensorByPath(productId, sensorPath, out var sensor),
                $"Sensor {sensorPath} not found");

            var force = InitiatorInfo.AsSystemForce("test_add_manual_ttl");

            // Id = Guid.Empty signals "new TTL policy" to UpdateTTLs.
            var ttlUpdate = new PolicyUpdate
            {
                Id = Guid.Empty,
                TTL = ttl.Ticks,
                Initiator = force,
                ConfirmationPeriod = 0,
                Conditions = [],
            };

            var sensorUpdate = new SensorUpdate
            {
                Id = sensor.Id,
                TTLPolicies = [ttlUpdate],
                Initiator = force,
            };

            var result = await _valuesCache.UpdateSensorAsync(sensorUpdate);
            Assert.True(result.IsOk, result.Error);
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

        private AlertTemplateModel BuildTemplateWithRegularPolicy(SensorType sensorType, string pathPattern)
        {
            var policy = Policy.BuildPolicy((byte)sensorType);
            var update = new PolicyUpdate
            {
                ConfirmationPeriod = 0,
                Conditions =
                [
                    new PolicyConditionUpdate(
                        PolicyOperation.GreaterThan,
                        PolicyProperty.Value,
                        new TargetValue(TargetType.Const, "0")),
                ],
            };
            policy.UpdatePolicy(update);

            var template = new AlertTemplateModel
            {
                Name = $"Test template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = (byte)sensorType,
                Paths = [pathPattern],
                Policies = [policy],
            };
            template.TryApplyPathTemplates(out _);

            return template;
        }
    }
}
