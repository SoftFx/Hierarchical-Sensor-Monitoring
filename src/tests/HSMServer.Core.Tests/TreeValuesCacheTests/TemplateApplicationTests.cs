using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Journal;
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

        // Failure injection for the persist-failure test (the #1394/#1401
        // suites' FailingDatabaseCore seam, keyed to one sensor so only the
        // targeted apply's UpdateSensor throws).
        private Guid _failSensorId = Guid.Empty;

        public TemplateApplicationTests(TemplateApplicationFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        protected override IDatabaseCore WrapDatabase(IDatabaseCore inner) =>
            new FailingDatabaseCore(inner, entity => _failSensorId != Guid.Empty && entity.Id == _failSensorId.ToString());


        // The journal seam the cache wires at sensor creation (the #1394
        // suites' CaptureJournalAsync pattern): captures every record the
        // action emits, including ones written on the queue threads.
        private async Task<List<JournalRecordModel>> CaptureJournalAsync(Func<Task> action)
        {
            var records = new ConcurrentBag<JournalRecordModel>();

            void OnRecord(JournalRecordModel record) => records.Add(record);

            _journalService.NewRecordEvent += OnRecord;
            try
            {
                await action();
            }
            finally
            {
                _journalService.NewRecordEvent -= OnRecord;
            }

            return [.. records];
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

            var (success, error, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (success, error, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (s1, e1, _) = await _valuesCache.AddAlertTemplateAsync(template1);
            Assert.True(s1, $"Failed to add template 1: {e1}");
            var (s2, e2, _) = await _valuesCache.AddAlertTemplateAsync(template2);
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

            var (success, error, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (success, error, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (addA, errA, _) = await _valuesCache.AddAlertTemplateAsync(templateA);
            Assert.True(addA, $"Failed to add template A: {errA}");
            var (addB, errB, _) = await _valuesCache.AddAlertTemplateAsync(templateB);
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

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(templateA);
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

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(edited);
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

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
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

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");
            await Task.Delay(300);

            // Assert: the regular template policy is gone from the sensor.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            Assert.DoesNotContain(sensorAfter.Policies, p => p.TemplateId == template.Id);
        }

        // Regression for #1606: disabling a policy through a template edit must
        // reach the sensor policies the template already applied. The re-apply
        // arm used to keep the sensor's IsDisabled unconditionally, so an alert
        // kept firing although the template said isDisabled: true.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_DisablePolicy_PropagatesToAppliedSensorPolicy()
        {
            // Arrange: template with one enabled regular policy, applied to a sensor.
            var template = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorToggleDisable");

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorToggleDisable";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorBefore));
            var applied = Assert.Single(sensorBefore.Policies, p => p.TemplateId == template.Id);
            Assert.False(applied.IsDisabled);

            // Act: edit the template so the same policy is disabled (the #1606
            // flow: update_alert_template with policies[0].isDisabled = true).
            template.Policies[0].SetDisabled(true);

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the already-applied sensor policy is disabled too — in
            // memory AND in the persisted entity shape (TryUpdateSensor writes
            // sensor.ToEntity() on every update; the #1394 lessons live here).
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedAfter = Assert.Single(sensorAfter.Policies, p => p.TemplateId == template.Id);
            Assert.True(appliedAfter.IsDisabled);
            Assert.True(appliedAfter.ToEntity().IsDisabled);
        }

        // The symmetric direction of the #1606 propagation: a policy created
        // disabled (the issue's control case) must be re-enabled on the applied
        // sensors when the template is edited back to isDisabled: false.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_EnablePolicy_PropagatesToAppliedSensorPolicy()
        {
            // Arrange: template with one disabled regular policy (disabled at
            // creation — the case that already worked before #1606).
            var template = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorToggleEnable");
            template.Policies[0].SetDisabled(true);

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorToggleEnable";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorBefore));
            var applied = Assert.Single(sensorBefore.Policies, p => p.TemplateId == template.Id);
            Assert.True(applied.IsDisabled);

            // Act: edit the template so the policy is enabled.
            template.Policies[0].SetDisabled(false);

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the already-applied sensor policy is enabled too.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedAfter = Assert.Single(sensorAfter.Policies, p => p.TemplateId == template.Id);
            Assert.False(appliedAfter.IsDisabled);
        }

        // #1077: the per-sensor toggle keeps winning over the template — but
        // only for the toggle itself. A user-made disable stamps a User
        // change-table owner; the re-apply now re-applies the template
        // CONTENT (the #1077 fix — it used to freeze the whole policy) with
        // the operator's IsDisabled preserved and the owner kept as the
        // toggle marker. The divergence is REPORTED only when the save
        // carries new information (#1077 review): a content change or a
        // template isDisabled flip — an UNCHANGED re-save like this one
        // stays quiet instead of re-opening the warning modal on every save.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserDisabledSensorPolicy_SurvivesTemplateReapply()
        {
            // Arrange: template with one enabled regular policy, applied.
            var template = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorToggleUserWins");

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorToggleUserWins";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var applied = Assert.Single(sensor.Policies, p => p.TemplateId == template.Id);
            Assert.False(applied.IsDisabled);

            // The operator's per-sensor toggle: a plain user initiator may only
            // change IsDisabled on a template-owned policy, and that toggle
            // stamps the User change-table owner.
            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                Policies = [new PolicyUpdate(applied, user) { IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);

            Assert.True(applied.IsDisabled);
            Assert.Equal(InitiatorType.User, sensor.ChangeTable.Policies[applied.Id.ToString()].Initiator.Type);

            // Act: re-apply the template UNCHANGED — its policy still
            // enabled, differing from the sensor's user-chosen true: the
            // content re-applies and the toggle survives (#1077).
            var (editOk, editError, editWarnings) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the operator's disable survives the re-apply.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedAfter = Assert.Single(sensorAfter.Policies, p => p.TemplateId == template.Id);
            Assert.True(appliedAfter.IsDisabled);
            Assert.Equal(InitiatorType.User, sensorAfter.ChangeTable.Policies[appliedAfter.Id.ToString()].Initiator.Type);

            // #1077 review: an unchanged re-save carries no new information —
            // neither the content nor the template's isDisabled moved — so
            // the preserved toggle stays quiet (no per-save warning/journal
            // spam). The divergence reports ride only the saves that change
            // something: pinned by the content-reapply test above and the
            // isDisabled-flip test below.
            Assert.DoesNotContain(editWarnings, w => w.Reason.Contains("preserved"));
        }


        // The #1077 headline case (the #1077 QA remark): after a per-sensor
        // toggle, a template edit's CONTENT changes — conditions, message —
        // must still land on the toggled policy; only IsDisabled keeps the
        // operator's value. Before the fix the whole re-apply was silently
        // skipped and the policy drifted from the template forever.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserDisabledSensorPolicy_TemplateContentReapplies_AndToggleSurvives()
        {
            // Arrange: template with one regular policy (Value > 100, message
            // "orig"), applied.
            var template = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorToggleContentReapply");

            // Destination/Schedule must be non-null: Policy.TryUpdate's
            // Destination.Update(null) throws (silently caught by
            // UpdatePolicy), which is why the plain builder helper lands an
            // empty-content policy — this test needs real content.
            template.Policies[0].UpdatePolicy(new PolicyUpdate
            {
                ConfirmationPeriod = 0,
                Template = "orig",
                Destination = new PolicyDestinationUpdate(),
                Schedule = new PolicyScheduleUpdate(),
                Conditions =
                [
                    new PolicyConditionUpdate(
                        PolicyOperation.GreaterThan,
                        PolicyProperty.Value,
                        new TargetValue(TargetType.Const, "100")),
                ],
            });

            Assert.Equal("orig", template.Policies[0].Template);

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorToggleContentReapply";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var applied = Assert.Single(sensor.Policies, p => p.TemplateId == template.Id);
            Assert.Equal("orig", applied.Template);

            // The operator's per-sensor disable toggle — stamps the User
            // change-table owner.
            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                Policies = [new PolicyUpdate(applied, user) { IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);
            Assert.True(applied.IsDisabled);

            // Act: edit the template — the condition target moves to 250 and
            // the message to "edited"; the entry stays enabled (isDisabled
            // = false), diverging from the operator's true.
            template.Policies[0].UpdatePolicy(new PolicyUpdate
            {
                ConfirmationPeriod = 0,
                Template = "edited",
                Destination = new PolicyDestinationUpdate(),
                Schedule = new PolicyScheduleUpdate(),
                Conditions =
                [
                    new PolicyConditionUpdate(
                        PolicyOperation.GreaterThan,
                        PolicyProperty.Value,
                        new TargetValue(TargetType.Const, "250")),
                ],
            });

            var (editOk, editError, editWarnings) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the SAME policy (matched by TemplateAlertId) now
            // carries the template's new content, the operator's disable
            // survives, and the owner stays User as the toggle marker.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedAfter = Assert.Single(sensorAfter.Policies, p => p.TemplateId == template.Id);
            Assert.Equal(applied.Id, appliedAfter.Id);

            Assert.Equal("edited", appliedAfter.Template);
            var condition = Assert.Single(appliedAfter.Conditions);
            Assert.Equal("250", condition.Target.Value.ToString());

            Assert.True(appliedAfter.IsDisabled);
            Assert.Equal(InitiatorType.User, sensorAfter.ChangeTable.Policies[appliedAfter.Id.ToString()].Initiator.Type);

            // The preserved toggle is REPORTED, never silent: one warning
            // naming the sensor and the divergence from the template.
            var preserved = Assert.Single(editWarnings, w => w.Reason.Contains("preserved"));
            Assert.Contains(sensorAfter.FullPath, preserved.SensorPath);
            Assert.Contains(applied.Id.ToString(), preserved.Reason);
        }


        // #1077: when the operator's preserved choice HAPPENS to equal the
        // template's isDisabled there is no disagreement to report — the
        // content re-applies quietly, no warning.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserToggleMatchingTemplateIsDisabled_NoWarning()
        {
            var template = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorToggleAgree");

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorToggleAgree";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var applied = Assert.Single(sensor.Policies, p => p.TemplateId == template.Id);

            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                Policies = [new PolicyUpdate(applied, user) { IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);

            // Act: edit the template with its entry DISABLED too — the
            // operator's preserved true and the template's true agree.
            template.Policies[0].SetDisabled(true);

            var (editOk, editError, editWarnings) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the toggle survives (still the operator's entry, not a
            // template re-sync) but nothing is reported — no divergence.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedAfter = Assert.Single(sensorAfter.Policies, p => p.TemplateId == template.Id);
            Assert.True(appliedAfter.IsDisabled);
            Assert.Equal(InitiatorType.User, sensorAfter.ChangeTable.Policies[appliedAfter.Id.ToString()].Initiator.Type);
            Assert.DoesNotContain(editWarnings, w => w.Reason.Contains("preserved"));
        }

        // The TTL twin of the #1606 propagation: disabling a template's TTL
        // entry through a template edit must reach the already-applied sensor
        // TTL policy.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_DisableTtlEntry_PropagatesToAppliedSensorTtlPolicy()
        {
            // Arrange: template with one TTL entry, applied to a sensor.
            var template = BuildTemplate(SensorType.Integer, "*/sensorTtlToggleDisable",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorTtlToggleDisable";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorBefore));
            var appliedTtl = Assert.Single(sensorBefore.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.False(appliedTtl.IsDisabled);

            // Act: edit the template so the TTL entry is disabled.
            template.TtlEntries[0].Policy.SetDisabled(true);

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the already-applied sensor TTL policy is disabled too —
            // in memory AND in the persisted entity shape (the TTL policies
            // ride the sensor entity itself).
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedTtlAfter = Assert.Single(sensorAfter.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.True(appliedTtlAfter.IsDisabled);
            Assert.True(appliedTtlAfter.ToEntity().IsDisabled);
            Assert.Contains(sensorAfter.ToEntity().TTLPolicies, e => e.IsDisabled);
        }

        // The TTL twin of the per-sensor protection (#1077): a user-made TTL
        // disable keeps its User change-table owner and survives the template
        // re-apply — the content re-applies with the operator's IsDisabled
        // preserved (it used to freeze the whole TTL update, all-or-nothing
        // per node). An UNCHANGED re-save stays quiet (#1077 review): the
        // divergence report fires only on saves that carry new information.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserDisabledSensorTtl_SurvivesTemplateReapply()
        {
            // Arrange: template with one TTL entry, applied to a sensor.
            var template = BuildTemplate(SensorType.Integer, "*/sensorTtlToggleUserWins",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorTtlToggleUserWins";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var appliedTtl = Assert.Single(sensor.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.False(appliedTtl.IsDisabled);

            // The operator's per-sensor TTL toggle (the documented user path:
            // re-assert the entry with its interval, only IsDisabled changed).
            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                TTLPolicies = [new PolicyUpdate(appliedTtl, user) { TTL = appliedTtl.TTLTicks, IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);

            Assert.True(appliedTtl.IsDisabled);
            Assert.Equal(InitiatorType.User, sensor.ChangeTable.TtlPolicies[appliedTtl.Id.ToString()].Initiator.Type);

            // Act: re-apply the template UNCHANGED — its TTL entry still
            // enabled, diverging from the sensor's user-chosen true: the
            // content re-applies and the toggle survives (#1077).
            var (editOk, editError, editWarnings) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the operator's disable survives the re-apply.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedTtlAfter = Assert.Single(sensorAfter.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.True(appliedTtlAfter.IsDisabled);
            Assert.Equal(InitiatorType.User, sensorAfter.ChangeTable.TtlPolicies[appliedTtlAfter.Id.ToString()].Initiator.Type);

            // #1077 review: the unchanged re-save reports nothing — the
            // interval and the template's isDisabled both stand still, so
            // the preserved toggle stays quiet (the interval-edit twin above
            // pins the loud arm: a real content change reports).
            Assert.DoesNotContain(editWarnings, w => w.Reason.Contains("preserved"));
        }


        // The TTL content case of #1077: after a per-sensor toggle, a
        // template edit's new INTERVAL must still land on the toggled TTL
        // policy; only IsDisabled keeps the operator's value.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserDisabledSensorTtl_TemplateIntervalReapplies_AndToggleSurvives()
        {
            // Arrange: template with one TTL entry (5 min), applied.
            var template = BuildTemplate(SensorType.Integer, "*/sensorTtlContentReapply",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorTtlContentReapply";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var appliedTtl = Assert.Single(sensor.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.Equal(TimeSpan.FromMinutes(5).Ticks, appliedTtl.TTLTicks);

            // The operator's per-sensor TTL disable toggle.
            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                TTLPolicies = [new PolicyUpdate(appliedTtl, user) { TTL = appliedTtl.TTLTicks, IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);
            Assert.True(appliedTtl.IsDisabled);

            // Act: edit the template's TTL entry to 15 minutes — the same
            // entry policy (matched by TemplateAlertId), a new interval.
            template.TtlEntries[0] = new TtlEntry(
                template.TtlEntries[0].Policy,
                new TimeIntervalModel(TimeSpan.FromMinutes(15).Ticks));

            var (editOk, editError, editWarnings) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the interval landed, the toggle survived, the owner
            // stays User as the toggle marker.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedTtlAfter = Assert.Single(sensorAfter.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.Equal(appliedTtl.Id, appliedTtlAfter.Id);

            Assert.Equal(TimeSpan.FromMinutes(15).Ticks, appliedTtlAfter.TTLTicks);
            Assert.True(appliedTtlAfter.IsDisabled);
            Assert.Equal(InitiatorType.User, sensorAfter.ChangeTable.TtlPolicies[appliedTtlAfter.Id.ToString()].Initiator.Type);

            var preserved = Assert.Single(editWarnings, w => w.Reason.Contains("preserved"));
            Assert.Contains(sensorAfter.FullPath, preserved.SensorPath);
        }


        // #1077 collateral fix: the TTL gate used to be all-or-nothing per
        // NODE — one toggled entry froze EVERY TTL entry of the re-apply.
        // With per-entry preservation both entries update: the toggled one
        // keeps its operator IsDisabled, the untouched one re-syncs the
        // template's flag.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserToggledOneTtlEntry_OtherEntryStillReapplies()
        {
            // Arrange: a template with TWO TTL entries (5 and 10 minutes).
            var template = new AlertTemplateModel
            {
                Name = $"Test template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = (byte)SensorType.Integer,
                Paths = ["*/sensorTtlMixedReapply"],
                TtlEntries =
                [
                    BuildTtlEntry(TimeSpan.FromMinutes(5)),
                    BuildTtlEntry(TimeSpan.FromMinutes(10)),
                ],
            };
            template.TryApplyPathTemplates(out _);

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorTtlMixedReapply";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, $"Failed to add sensor value: {addResult.Error}");
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            Assert.Equal(2, sensor.Policies.TTLPolicies.Count(t => t.TemplateId == template.Id));

            // The operator disables ONLY the first entry (matched later by
            // its TemplateAlertId).
            var first = sensor.Policies.TTLPolicies.Single(t => t.TemplateAlertId == template.TtlEntries[0].Policy.Id);
            var second = sensor.Policies.TTLPolicies.Single(t => t.TemplateAlertId == template.TtlEntries[1].Policy.Id);

            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                TTLPolicies = [new PolicyUpdate(first, user) { TTL = first.TTLTicks, IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);
            Assert.True(first.IsDisabled);
            Assert.False(second.IsDisabled);

            // Act: edit both intervals in the template.
            template.TtlEntries[0] = new TtlEntry(template.TtlEntries[0].Policy,
                new TimeIntervalModel(TimeSpan.FromMinutes(15).Ticks));
            template.TtlEntries[1] = new TtlEntry(template.TtlEntries[1].Policy,
                new TimeIntervalModel(TimeSpan.FromMinutes(20).Ticks));

            var (editOk, editError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: BOTH entries took their new intervals; the toggled one
            // keeps its disable, the untouched one follows the template.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var firstAfter = sensorAfter.Policies.TTLPolicies.Single(t => t.Id == first.Id);
            var secondAfter = sensorAfter.Policies.TTLPolicies.Single(t => t.Id == second.Id);

            Assert.Equal(TimeSpan.FromMinutes(15).Ticks, firstAfter.TTLTicks);
            Assert.True(firstAfter.IsDisabled);

            Assert.Equal(TimeSpan.FromMinutes(20).Ticks, secondAfter.TTLTicks);
            Assert.False(secondAfter.IsDisabled);
        }

        // #1077 review (P2): a template apply carries only ITS OWN TTL
        // entries — re-saving an UNRELATED template must not re-stamp the
        // change-table owner of this template's toggled TTL entry. The
        // pre-fix non-listed re-stamp in BaseNodeModel.Update downgraded the
        // User toggle marker to AlertTemplate, and the NEXT save of the
        // owning template then passed the CanChange pre-check and silently
        // re-enabled the operator's disable.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserDisabledTtl_OtherTemplateResave_DoesNotReleaseTheToggle()
        {
            // Arrange: template A (one regular policy, no TTL entries) and
            // template B (one TTL entry), both matching the same sensor.
            var templateA = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorTwoTemplateToggle");
            var templateB = BuildTemplate(SensorType.Integer, "*/sensorTwoTemplateToggle",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addA, errA, _) = await _valuesCache.AddAlertTemplateAsync(templateA);
            Assert.True(addA, $"Failed to add template A: {errA}");
            var (addB, errB, _) = await _valuesCache.AddAlertTemplateAsync(templateB);
            Assert.True(addB, $"Failed to add template B: {errB}");

            var sensorPath = "sensorTwoTemplateToggle";
            var sensorValue = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            var addResult = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, addResult.Error);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var ttlOfB = Assert.Single(sensor.Policies.TTLPolicies, t => t.TemplateId == templateB.Id);
            Assert.Single(sensor.Policies, p => p.TemplateId == templateA.Id);

            // The operator disables B's TTL — the User owner is the #1077
            // toggle marker.
            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                TTLPolicies = [new PolicyUpdate(ttlOfB, user) { TTL = ttlOfB.TTLTicks, IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);
            Assert.True(ttlOfB.IsDisabled);
            Assert.Equal(InitiatorType.User, sensor.ChangeTable.TtlPolicies[ttlOfB.Id.ToString()].Initiator.Type);

            // Act 1: re-save template A unchanged. Its TTL list is EMPTY
            // (the finding's shape: the empty list used to re-stamp every
            // non-listed TTL — B's included — down to AlertTemplate).
            var (resaveA, resaveAError, _) = await _valuesCache.AddAlertTemplateAsync(templateA);
            Assert.True(resaveA, $"Failed to re-save template A: {resaveAError}");

            // Assert 1: B's TTL keeps BOTH the operator's disable and the
            // User toggle marker — A's apply did not touch it.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var afterA));
            var ttlAfterA = Assert.Single(afterA.Policies.TTLPolicies, t => t.TemplateId == templateB.Id);
            Assert.True(ttlAfterA.IsDisabled);
            Assert.Equal(InitiatorType.User, afterA.ChangeTable.TtlPolicies[ttlAfterA.Id.ToString()].Initiator.Type);

            // Act 2: re-save template B unchanged — the save whose pre-fix
            // behaviour silently re-enabled the operator's disable.
            var (resaveB, resaveBError, _) = await _valuesCache.AddAlertTemplateAsync(templateB);
            Assert.True(resaveB, $"Failed to re-save template B: {resaveBError}");

            // Assert 2: the operator's disable survives B's own re-apply.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var afterB));
            var ttlAfterB = Assert.Single(afterB.Policies.TTLPolicies, t => t.TemplateId == templateB.Id);
            Assert.True(ttlAfterB.IsDisabled);
            Assert.Equal(InitiatorType.User, afterB.ChangeTable.TtlPolicies[ttlAfterB.Id.ToString()].Initiator.Type);
        }

        // A collector re-registration (AddOrUpdateSensor with TtlAlerts)
        // sends its own TTL alerts as empty-id entries — entries it does
        // not list (a template-owned TTL among them) survive verbatim in
        // UpdateTTLs, and so must their change-table owner: the non-listed
        // re-stamp in BaseNodeModel.Update respects change ownership, so
        // the User toggle marker of a template-owned TTL survives a
        // collector write, and the template's next save still preserves
        // and REPORTS the operator's disable instead of silently
        // re-enabling it.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserDisabledTtl_CollectorReregistration_DoesNotReleaseTheToggle()
        {
            // Arrange: template with one TTL entry (5 min), applied.
            var template = BuildTemplate(SensorType.Integer, "*/sensorTtlCollectorReassert",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorTtlCollectorReassert";
            var sensorValue = SensorValuesFactory.BuildSensorValue(
                SensorType.Integer, sensorPath, DateTime.UtcNow);

            var addResult = await _valuesCache.AddSensorValueAsync(
                _fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, addResult.Error);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var appliedTtl = Assert.Single(sensor.Policies.TTLPolicies, t => t.TemplateId == template.Id);

            // The operator's per-sensor TTL disable toggle — the User owner
            // is the toggle marker.
            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                TTLPolicies = [new PolicyUpdate(appliedTtl, user) { TTL = appliedTtl.TTLTicks, IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);
            Assert.True(appliedTtl.IsDisabled);
            Assert.Equal(InitiatorType.User, sensor.ChangeTable.TtlPolicies[appliedTtl.Id.ToString()].Initiator.Type);

            // Act 1: the collector re-registers its TTL alert — the
            // AddOrUpdateSensor shape for TtlAlerts: a DataCollector
            // initiator and an empty-id entry (its own alert), leaving the
            // template TTL unlisted.
            var collector = InitiatorInfo.AsCollector("collectorKey", isForce: false);
            var reregistration = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = collector,
                TTLPolicies =
                [
                    new PolicyUpdate
                    {
                        Id = Guid.Empty,
                        TTL = TimeSpan.FromMinutes(30).Ticks,
                        Destination = new PolicyDestinationUpdate(),
                        Initiator = collector,
                    },
                ],
            });
            Assert.True(reregistration.IsOk, reregistration.Error);

            // Assert 1: the template TTL keeps BOTH the operator's disable
            // and the User toggle marker — the collector write did not
            // touch it.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var afterCollector));
            var ttlAfterCollector = Assert.Single(afterCollector.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.True(ttlAfterCollector.IsDisabled);
            Assert.Equal(InitiatorType.User,
                afterCollector.ChangeTable.TtlPolicies[ttlAfterCollector.Id.ToString()].Initiator.Type);

            // Act 2: edit the template's TTL interval — new content landing
            // under the operator's preserved, diverging disable.
            template.TtlEntries[0] = new TtlEntry(
                template.TtlEntries[0].Policy,
                new TimeIntervalModel(TimeSpan.FromMinutes(15).Ticks));

            var (editOk, editError, editWarnings) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert 2: the interval landed, the disable survived, the owner
            // stays User, and the divergence rides the warnings channel.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedTtlAfter = Assert.Single(sensorAfter.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.Equal(TimeSpan.FromMinutes(15).Ticks, appliedTtlAfter.TTLTicks);
            Assert.True(appliedTtlAfter.IsDisabled);
            Assert.Equal(InitiatorType.User,
                sensorAfter.ChangeTable.TtlPolicies[appliedTtlAfter.Id.ToString()].Initiator.Type);

            var preserved = Assert.Single(editWarnings, w => w.Reason.Contains("preserved"));
            Assert.Contains(sensorAfter.FullPath, preserved.SensorPath);
        }

        // #1077 review: a higher-priority owner that is NOT a user — the
        // ServerMigration soft migrator stamping a template-owned TTL — is
        // machinery, not an operator toggle. The next apply of the template
        // reclaims the entry: the template's isDisabled lands, the ownership
        // returns to the template, and no phantom "operator's choice
        // preserved" is reported (the PR as shipped froze the flag and
        // reported the migration stamp as an operator toggle).
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_MigrationOwnedTtl_FollowsTemplate_NotFrozenAsOperatorToggle()
        {
            // Arrange: template with one TTL entry, applied.
            var template = BuildTemplate(SensorType.Integer, "*/sensorTtlMigrationOwner",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorTtlMigrationOwner";
            var sensorValue = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            var addResult = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, addResult.Error);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var appliedTtl = Assert.Single(sensor.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.False(appliedTtl.IsDisabled);

            // The migration stamp: a soft-migrator full-list TTL re-assert
            // (the MigrationManager write shape) re-stamps the entry's
            // change-table owner to ServerMigration (20) — above
            // AlertTemplate (15), but not an operator's choice.
            var migration = InitiatorInfo.AsSoftSystemMigrator();
            var reassert = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = migration,
                TTLPolicies = [new PolicyUpdate(appliedTtl, migration) { TTL = appliedTtl.TTLTicks }],
            });
            Assert.True(reassert.IsOk, reassert.Error);
            Assert.Equal(InitiatorType.ServerMigration, sensor.ChangeTable.TtlPolicies[appliedTtl.Id.ToString()].Initiator.Type);

            // Act: edit the template so the TTL entry is disabled and re-save.
            template.TtlEntries[0].Policy.SetDisabled(true);

            var (editOk, editError, editWarnings) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the entry follows the template — disabled — and the
            // ownership returns to AlertTemplate (the reclaim); nothing is
            // misreported as a preserved operator toggle.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var ttlAfter = Assert.Single(sensorAfter.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.True(ttlAfter.IsDisabled);
            Assert.Equal(InitiatorType.AlertTemplate, sensorAfter.ChangeTable.TtlPolicies[ttlAfter.Id.ToString()].Initiator.Type);
            Assert.DoesNotContain(editWarnings, w => w.Reason.Contains("preserved"));
        }

        // The REGULAR-policy twin of the migration-reclaim test above: the
        // TTL twin only exercises the BaseNodeModel.Update gate, while a
        // migration-owned REGULAR policy reclaims through the non-user
        // clause of SensorPolicyCollection.TryUpdate — a path no other test
        // reaches (the only other regular-policy coverage of the reclaim is
        // inference from the TTL arm).
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_MigrationOwnedPolicy_FollowsTemplate_NotFrozenAsOperatorToggle()
        {
            // Arrange: template with one regular policy (enabled), applied.
            var template = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorPolicyMigrationOwner");

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorPolicyMigrationOwner";
            var sensorValue = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            var addResult = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, addResult.Error);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var applied = Assert.Single(sensor.Policies, p => p.TemplateId == template.Id);
            Assert.False(applied.IsDisabled);

            // The migration stamp: a soft-migrator toggle on a template-owned
            // regular policy (the only write a non-force initiator reaches on
            // one) stamps the change-table owner to ServerMigration (20) —
            // above AlertTemplate (15), but not an operator's choice.
            var migration = InitiatorInfo.AsSoftSystemMigrator();
            var reassert = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = migration,
                Policies = [new PolicyUpdate(applied, migration) { IsDisabled = true }],
            });
            Assert.True(reassert.IsOk, reassert.Error);
            Assert.True(applied.IsDisabled);
            Assert.Equal(InitiatorType.ServerMigration, sensor.ChangeTable.Policies[applied.Id.ToString()].Initiator.Type);

            // Act: re-save the template UNCHANGED — its entry stays enabled,
            // diverging from the migration's disable: the apply must reclaim
            // the entry instead of freezing it as a phantom operator toggle.
            var (editOk, editError, editWarnings) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(editOk, $"Failed to edit template: {editError}");

            // Assert: the policy follows the template — re-enabled — and the
            // ownership returns to AlertTemplate (the reclaim); nothing is
            // misreported as a preserved operator toggle.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedAfter = Assert.Single(sensorAfter.Policies, p => p.TemplateId == template.Id);
            Assert.Equal(applied.Id, appliedAfter.Id);
            Assert.False(appliedAfter.IsDisabled);
            Assert.Equal(InitiatorType.AlertTemplate, sensorAfter.ChangeTable.Policies[appliedAfter.Id.ToString()].Initiator.Type);
            Assert.DoesNotContain(editWarnings, w => w.Reason.Contains("preserved"));
        }

        // #1077 review: the divergence report also fires when the save's new
        // information is the template's OWN isDisabled flip — the admin
        // re-enabled the entry while the operator's preserved disable
        // disagrees with it, and that disagreement must not pass silently.
        // The saves go through fresh template instances (the production
        // controller shape) so the previous-template diff sees the flip.
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserDisabledSensorPolicy_TemplateIsDisabledFlip_ReportsDivergence()
        {
            // Arrange: template with one enabled regular policy, applied.
            var template = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorToggleFlipReport");

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorToggleFlipReport";
            var sensorValue = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            var addResult = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, addResult.Error);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var applied = Assert.Single(sensor.Policies, p => p.TemplateId == template.Id);

            // The operator's per-sensor disable toggle.
            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                Policies = [new PolicyUpdate(applied, user) { IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);
            Assert.True(applied.IsDisabled);

            // Act 1: save the template with the entry DISABLED — the flip
            // agrees with the operator's preserved disable, so nothing is
            // reported.
            var agreeingSave = CloneTemplateWithPolicy(template, policyDisabled: true);
            var (ok1, err1, warnings1) = await _valuesCache.AddAlertTemplateAsync(agreeingSave);
            Assert.True(ok1, $"Failed to save the agreeing template: {err1}");
            Assert.DoesNotContain(warnings1, w => w.Reason.Contains("preserved"));

            // Act 2: flip the template's entry back to ENABLED — the save
            // carries new information (the isDisabled flip) while the
            // operator's preserved disable diverges from it: the divergence
            // is reported, the toggle still survives.
            var divergingSave = CloneTemplateWithPolicy(template, policyDisabled: false);
            var (ok2, err2, warnings2) = await _valuesCache.AddAlertTemplateAsync(divergingSave);
            Assert.True(ok2, $"Failed to save the diverging template: {err2}");

            // Assert: the toggle survives and the divergence rides the
            // warnings channel, naming the sensor and the policy.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedAfter = Assert.Single(sensorAfter.Policies, p => p.TemplateId == template.Id);
            Assert.True(appliedAfter.IsDisabled);
            Assert.Equal(InitiatorType.User, sensorAfter.ChangeTable.Policies[appliedAfter.Id.ToString()].Initiator.Type);

            var preserved = Assert.Single(warnings2, w => w.Reason.Contains("preserved"));
            Assert.Contains(sensorAfter.FullPath, preserved.SensorPath);
            Assert.Contains(appliedAfter.Id.ToString(), preserved.Reason);
        }

        // #1077 review: the buffered reports are emitted only AFTER the apply
        // PERSISTED (TryUpdateSensor == true) — with the sensor-row write
        // failing, a diverging operator toggle that the same save shape
        // otherwise reports (see the content-reapply test above) leaves NO
        // "operator toggle preserved" journal record or warning: a record
        // must describe state the apply actually landed. The failure rides
        // the #1394 hard channel (false + products-naming error).
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserDisabledSensorPolicy_ApplyPersistFails_NoPreservedToggleReport()
        {
            // Arrange: template with one regular policy (Value > 100, message
            // "orig"), applied — the same shape the content-reapply test uses,
            // so the divergence WOULD be reported on a persisting save.
            var template = BuildTemplateWithRegularPolicy(SensorType.Integer, "*/sensorTogglePersistFail");
            template.Policies[0].UpdatePolicy(new PolicyUpdate
            {
                ConfirmationPeriod = 0,
                Template = "orig",
                Destination = new PolicyDestinationUpdate(),
                Schedule = new PolicyScheduleUpdate(),
                Conditions =
                [
                    new PolicyConditionUpdate(
                        PolicyOperation.GreaterThan,
                        PolicyProperty.Value,
                        new TargetValue(TargetType.Const, "100")),
                ],
            });

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorTogglePersistFail";
            var sensorValue = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            var addResult = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, addResult.Error);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var applied = Assert.Single(sensor.Policies, p => p.TemplateId == template.Id);
            Assert.False(applied.IsDisabled);

            // The operator's per-sensor disable toggle — the User owner is
            // the #1077 toggle marker, and the toggle diverges from the
            // template's enabled entry.
            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                Policies = [new PolicyUpdate(applied, user) { IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);
            Assert.True(applied.IsDisabled);

            // Edit the template's content (target 100 -> 250) so the next
            // apply carries the new information the report gate requires —
            // then fail the apply's sensor-row persist.
            template.Policies[0].UpdatePolicy(new PolicyUpdate
            {
                ConfirmationPeriod = 0,
                Template = "edited",
                Destination = new PolicyDestinationUpdate(),
                Schedule = new PolicyScheduleUpdate(),
                Conditions =
                [
                    new PolicyConditionUpdate(
                        PolicyOperation.GreaterThan,
                        PolicyProperty.Value,
                        new TargetValue(TargetType.Const, "250")),
                ],
            });

            _failSensorId = sensor.Id;

            (bool Success, string Error, List<TemplateApplyWarning> Warnings) result = default;
            var records = await CaptureJournalAsync(async () =>
                result = await _valuesCache.AddAlertTemplateAsync(template));

            var (editOk, editError, editWarnings) = result;

            // The #1394 hard channel: the save FAILED naming the product, and
            // warnings never ride the conflict.
            Assert.False(editOk);
            Assert.Contains("TemplateTestProduct", editError);
            Assert.Empty(editWarnings);

            // The load-bearing assertion: no "operator toggle preserved"
            // journal record describes an apply that never persisted —
            // although the diverging toggle existed and the save carried a
            // content change (the exact shape the content-reapply test above
            // proves is reported when the persist succeeds).
            Assert.DoesNotContain(records, r => r.NewValue.Contains("operator toggle preserved"));
        }

        // The TTL twin of the isDisabled-flip report: the flip rides a TTL
        // ENTRY, exercising the TtlEntries half of the previous-template
        // diff (the regular twin above only covers the Policies half).
        [Fact]
        [Trait("Category", "Template application")]
        public async Task EditTemplate_UserDisabledSensorTtl_TemplateIsDisabledFlip_ReportsDivergence()
        {
            // Arrange: template with one TTL entry (enabled), applied.
            var template = BuildTemplate(SensorType.Integer, "*/sensorTtlFlipReport",
                ttlInterval: TimeSpan.FromMinutes(5));

            var (addOk, addError, _) = await _valuesCache.AddAlertTemplateAsync(template);
            Assert.True(addOk, $"Failed to add template: {addError}");

            var sensorPath = "sensorTtlFlipReport";
            var sensorValue = SensorValuesFactory.BuildSensorValue(SensorType.Integer, sensorPath, DateTime.UtcNow);
            var addResult = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyId, _fixture.ProductId, sensorValue);
            Assert.True(addResult.IsOk, addResult.Error);
            await Task.Delay(200);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensor));
            var appliedTtl = Assert.Single(sensor.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.False(appliedTtl.IsDisabled);

            // The operator's per-sensor TTL disable toggle.
            var user = InitiatorInfo.AsUser("operator");
            var toggle = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = user,
                TTLPolicies = [new PolicyUpdate(appliedTtl, user) { TTL = appliedTtl.TTLTicks, IsDisabled = true }],
            });
            Assert.True(toggle.IsOk, toggle.Error);
            Assert.True(appliedTtl.IsDisabled);

            // Act 1: save the template with the TTL entry DISABLED — the
            // flip agrees with the operator's preserved disable, so nothing
            // is reported.
            var agreeingSave = CloneTemplateWithTtlEntry(template, ttlDisabled: true);
            var (ok1, err1, warnings1) = await _valuesCache.AddAlertTemplateAsync(agreeingSave);
            Assert.True(ok1, $"Failed to save the agreeing template: {err1}");
            Assert.DoesNotContain(warnings1, w => w.Reason.Contains("preserved"));

            // Act 2: flip the template's TTL entry back to ENABLED — the
            // save carries new information (the isDisabled flip on the TTL
            // entry) while the operator's preserved disable diverges from
            // it: the divergence is reported, the toggle still survives.
            var divergingSave = CloneTemplateWithTtlEntry(template, ttlDisabled: false);
            var (ok2, err2, warnings2) = await _valuesCache.AddAlertTemplateAsync(divergingSave);
            Assert.True(ok2, $"Failed to save the diverging template: {err2}");

            // Assert: the toggle survives and the divergence rides the
            // warnings channel, naming the sensor and the TTL policy.
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductId, sensorPath, out var sensorAfter));
            var appliedTtlAfter = Assert.Single(sensorAfter.Policies.TTLPolicies, t => t.TemplateId == template.Id);
            Assert.True(appliedTtlAfter.IsDisabled);
            Assert.Equal(InitiatorType.User, sensorAfter.ChangeTable.TtlPolicies[appliedTtlAfter.Id.ToString()].Initiator.Type);

            var preserved = Assert.Single(warnings2, w => w.Reason.Contains("preserved"));
            Assert.Contains(sensorAfter.FullPath, preserved.SensorPath);
            Assert.Contains(appliedTtlAfter.Id.ToString(), preserved.Reason);
        }

        // A fresh AlertTemplateModel per save with the SAME policy id and
        // content (the controller's data.ToModel shape) — mutating a shared
        // template instance would hide the isDisabled flip from the
        // previous-template diff AddAlertTemplateAsync computes.
        private static AlertTemplateModel CloneTemplateWithPolicy(AlertTemplateModel source, bool policyDisabled)
        {
            var entity = source.Policies[0].ToEntity() with { IsDisabled = policyDisabled };

            var policy = Policy.BuildPolicy(source.SensorType);
            policy.Apply(entity);

            var clone = new AlertTemplateModel
            {
                Id = source.Id,
                Name = source.Name,
                FolderId = source.FolderId,
                SensorType = source.SensorType,
                Paths = [.. source.Paths],
                Policies = [policy],
            };
            clone.TryApplyPathTemplates(out _);

            return clone;
        }

        // The TTL sibling of CloneTemplateWithPolicy: a fresh
        // AlertTemplateModel per save with the SAME TTL entry policy id and
        // interval, only the entry's isDisabled differing — so the
        // previous-template diff sees the flip on the TTL entry.
        private static AlertTemplateModel CloneTemplateWithTtlEntry(AlertTemplateModel source, bool ttlDisabled)
        {
            var entry = source.TtlEntries[0];
            var entity = entry.Policy.ToEntity() with { IsDisabled = ttlDisabled };

            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(entry.Interval);

            var policy = new TTLPolicy(ttlSetting, entity);

            var clone = new AlertTemplateModel
            {
                Id = source.Id,
                Name = source.Name,
                FolderId = source.FolderId,
                SensorType = source.SensorType,
                Paths = [.. source.Paths],
                TtlEntries = [new TtlEntry(policy, entry.Interval)],
            };
            clone.TryApplyPathTemplates(out _);

            return clone;
        }

        private static TtlEntry BuildTtlEntry(TimeSpan interval)
        {
            var ttlSetting = new TimeIntervalSettingProperty();
            ttlSetting.TrySetValue(new TimeIntervalModel(interval.Ticks));

            return new TtlEntry(new TTLPolicy(ttlSetting, null), ttlSetting.Value ?? TimeIntervalModel.None);
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
                // Destination/Schedule arms ride as empty updates — a null
                // arm used to throw inside PolicyDestination.Update, and
                // Policy.UpdatePolicy swallows TryUpdate's error, minting a
                // CONTENT-EMPTY policy without a whisper (#1645).
                Destination = new PolicyDestinationUpdate(),
                Schedule = new PolicyScheduleUpdate(),
                Conditions =
                [
                    new PolicyConditionUpdate(
                        PolicyOperation.GreaterThan,
                        PolicyProperty.Value,
                        new TargetValue(TargetType.Const, "0")),
                ],
            };
            policy.UpdatePolicy(update);

            // UpdatePolicy discards TryUpdate's error by contract — assert
            // the content landed so the helper can never regress silently
            // (#1645): a dozen template-application tests build on it.
            Assert.Single(policy.Conditions);
            Assert.Equal(PolicyProperty.Value, policy.Conditions[0].Property);

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
