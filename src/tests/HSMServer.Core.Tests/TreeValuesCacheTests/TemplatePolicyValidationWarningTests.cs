using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
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
    // #1401: a template save that PERSISTS but has a matching sensor REJECT an
    // individual policy must surface the rejection instead of swallowing it —
    // the rejected alert is never created (not on the sensor, not in the DB),
    // and before #1401 the only trace was one log line. Pinned here against
    // the real cache + journal wiring:
    //  - the save answers SUCCESS with a warning per rejected policy (sensor
    //    path + reason), never a failure;
    //  - every rejection leaves a journal record naming the sensor and the
    //    reason (the #1394 "Alert" record shape);
    //  - the HARD failure channel (sensor missing / DB write failure) keeps
    //    the #1394 contract exactly: (false, products-naming error) with NO
    //    warnings riding the conflict.
    [Collection("Database collection")]
    public class TemplatePolicyValidationWarningTests : TemplateFailureTestsBase
    {
        private readonly TemplateConcurrencyFixture _fixture;


        public TemplatePolicyValidationWarningTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture)
        {
            _fixture = fixture;
        }


        // The journal seam the cache wires at sensor creation (the #1394
        // suites' CaptureJournalAsync pattern).
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

        private async Task CreateSensor(string path, SensorType type)
        {
            var value = SensorValuesFactory.BuildSensorValue(type, path, DateTime.UtcNow);
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            await Task.Delay(300);
        }

        // AnyType template carrying ONE regular policy conditioned on
        // (operation, property, target). The condition is PLANTED directly on
        // the carrier policy — the AlertScheduleDetachTests recipe for
        // "constructible but un-rebuildable on the target type" — because a
        // BooleanPolicy (the AnyType stand-in the editor and the API build)
        // cannot itself carry a Value condition. The carrier is just a
        // condition bag: matching uses template.SensorType, and
        // ApplyTemplateToSensor copies the conditions verbatim into the
        // per-sensor update, where the SENSOR's own policy type rebuilds them.
        private AlertTemplateModel BuildAnyTypeTemplate(List<string> paths, Guid policyId,
            PolicyOperation operation = PolicyOperation.GreaterThan,
            PolicyProperty property = PolicyProperty.Value,
            TargetValue target = null)
        {
            target ??= new TargetValue(TargetType.Const, "0");

            var policy = Policy.BuildPolicy((byte)SensorType.Integer);

            policy.UpdatePolicy(new PolicyUpdate { Id = policyId, ConfirmationPeriod = 0 });

            policy.Conditions.Add(new PolicyIntegerCondition<IntegerValue>
            {
                Operation = operation,
                Property = property,
                Target = target,
            });

            var template = new AlertTemplateModel
            {
                Name = $"Warning template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = AlertTemplateModel.AnyType,
                Paths = paths,
                Policies = [policy],
            };
            template.TryApplyPathTemplates(out _);

            return template;
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task AddTemplate_PolicyRejectedBySensorType_WarnsWithoutFailingSave()
        {
            // An Integer sensor accepts a Value condition; an IntegerBar
            // sensor does not (BarSensorPolicy.GetCondition throws for Value).
            const string integerPath = "warnPolicyInteger";
            const string barPath = "warnPolicyBar";

            await CreateSensor(integerPath, SensorType.Integer);
            await CreateSensor(barPath, SensorType.IntegerBar);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, integerPath, out var integerSensor));
            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, barPath, out var barSensor));

            var template = BuildAnyTypeTemplate([$"*/{integerPath}", $"*/{barPath}"], Guid.NewGuid());

            (bool Success, string Error, List<TemplateApplyWarning> Warnings) result = default;

            var records = await CaptureJournalAsync(async () =>
                result = await _valuesCache.AddAlertTemplateAsync(template));

            var (success, error, warnings) = result;

            // The save PERSISTED — the rejection must not fail it (a permanent
            // 409 over a save that worked was the deliberate #1396 split).
            Assert.True(success, error);

            // One warning per rejected policy: the bar sensor, with the
            // property-vs-type reason.
            var warning = Assert.Single(warnings);
            Assert.Contains(barPath, warning.SensorPath);
            Assert.Contains("Value", warning.Reason);

            // The rejection is real, not just reported: the Integer sensor
            // got the alert, the bar sensor did not.
            Assert.Contains(integerSensor.Policies, p => p.TemplateId == template.Id);
            Assert.DoesNotContain(barSensor.Policies, p => p.TemplateId == template.Id);

            // And the journal holds the per-policy record naming the sensor
            // and the reason (#1394's "Alert" record shape): the "alert isn't
            // there, nothing in the journal" symptom closes here.
            Assert.Contains(records, r => r.Key.Id == barSensor.Id
                                          && r.PropertyName == "Alert"
                                          && r.OldValue == string.Empty
                                          && r.NewValue.Contains("not applied")
                                          && r.NewValue.Contains(warning.Reason)
                                          && r.Path == barSensor.FullPath);
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task UpdateTemplate_PolicyRejectedOnExistingPolicy_WarnsAndJournals()
        {
            // The UPDATE arm of the apply: the sensor already holds the
            // template's policy and the edit's new condition is rejected by
            // the sensor's type — the alert is not re-created with the new
            // shape (the sensor keeps the previous version), and the caller
            // must hear it.
            const string barPath = "warnPolicyUpdateBar";

            await CreateSensor(barPath, SensorType.IntegerBar);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, barPath, out var sensor));

            var policyId = Guid.NewGuid();

            // First save: a Min condition — bar sensors offer it, so the bar
            // sensor gets the alert.
            var template = BuildAnyTypeTemplate([$"*/{barPath}"], policyId,
                PolicyOperation.GreaterThan, PolicyProperty.Min);

            Assert.True((await _valuesCache.AddAlertTemplateAsync(template)).Success);
            await Task.Delay(300);

            var applied = Assert.Single(sensor.Policies, p => p.TemplateId == template.Id);

            // Snapshot the applied shape BEFORE the edit: the UPDATE arm
            // mutates the live policy in place, so only a captured string can
            // prove a rejection left it intact.
            var appliedShape = applied.ToString();

            // Edit the SAME template id and policy id to a Value condition
            // the bar sensor's type does not offer: the update arm rejects it.
            var edited = BuildAnyTypeTemplate([$"*/{barPath}"], policyId);
            edited.Id = template.Id;

            (bool Success, string Error, List<TemplateApplyWarning> Warnings) result = default;

            var records = await CaptureJournalAsync(async () =>
                result = await _valuesCache.AddAlertTemplateAsync(edited));

            var (success, error, warnings) = result;

            Assert.True(success, error);

            var warning = Assert.Single(warnings);
            Assert.Contains(barPath, warning.SensorPath);
            Assert.Contains("Value", warning.Reason);

            // The sensor keeps the previously-applied alert EXACTLY as it was:
            // a rejection must not half-overwrite the live policy (new fields,
            // emptied conditions) while reporting "not applied" — the update
            // is all-or-nothing (#1542 review).
            Assert.Contains(sensor.Policies, p => p.Id == applied.Id);
            Assert.Equal(appliedShape, applied.ToString());

            var keptCondition = Assert.Single(applied.Conditions);
            Assert.Equal(PolicyProperty.Min, keptCondition.Property);
            Assert.Equal(PolicyOperation.GreaterThan, keptCondition.Operation);

            Assert.Contains(records, r => r.Key.Id == sensor.Id
                                          && r.PropertyName == "Alert"
                                          && r.NewValue.Contains("not applied"));
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task AddTemplate_DbApplyFailure_WarningsDoNotRideTheConflict()
        {
            // The HARD channel is untouched (#1396 keyed it to TryUpdateSensor's
            // return value): a DB write failure answers (false, products-naming
            // error) and the warnings list stays EMPTY — the 409 body keeps its
            // exact shape.
            const string sensorPath = "warnPolicyDbFail";

            await CreateSensor(sensorPath, SensorType.Integer);

            var template = BuildAnyTypeTemplate([$"*/{sensorPath}"], Guid.NewGuid());

            _failProductId = _fixture.ProductAId;

            var (success, error, warnings) = await _valuesCache.AddAlertTemplateAsync(template);

            Assert.False(success);
            Assert.Contains("ProductA_concurrency", error);
            Assert.Empty(warnings);
        }
    }
}
