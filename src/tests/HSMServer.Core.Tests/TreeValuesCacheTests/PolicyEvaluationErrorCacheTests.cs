using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Cache;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.Journal;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Model.Requests;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using Xunit;

namespace HSMServer.Core.Tests.TreeValuesCacheTests
{
    // #1563 review: the collection-layer pins live in
    // SensorPolicyCollectionTests, but the issue's actual failure was at the
    // CACHE layer — TryUpdateSensor returned false and skipped
    // _database.UpdateSensor. Pinned here on the real cache + journal +
    // database wiring, with the issue's own trigger (a Length condition over
    // a stored null string, whose executor dereferences StringValue.Value):
    //  - a SensorUpdate adding the throwing policy answers Ok and PERSISTS
    //    the sensor row (the DB write is not skipped over the soft failure);
    //  - a template whose policy throws at apply time reports an EVALUATION
    //    failure to the caller (one warning + a journal record saying
    //    "evaluation failed", never "not applied") while the policy is
    //    really applied and persisted — the pre-review code logged
    //    "partially applied" at Error and the caller heard nothing;
    //  - after a history clear (no last value), applying a HEALTHY template
    //    carries no stale evaluation warning from an earlier pass.
    [Collection("Database collection")]
    public class PolicyEvaluationErrorCacheTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private readonly TemplateConcurrencyFixture _fixture;


        public PolicyEvaluationErrorCacheTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;
        }


        // The journal seam the cache wires at sensor creation (the #1401
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

        // The issue's trigger sensor: a String sensor whose LAST value has
        // Value == null, so every evaluation of a Length>const policy throws.
        private async Task<BaseSensorModel> CreateNullStringSensor(string path)
        {
            var value = new HSMSensorDataObjects.SensorValueRequests.StringSensorValue { Path = path, Time = DateTime.UtcNow };
            await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            await Task.Delay(300);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out BaseSensorModel sensor));
            Assert.Null(((StringValue)sensor.LastValue).Value);

            return sensor;
        }

        // The issue's own alert shape: Length > 3 on a string sensor.
        private static PolicyUpdate BuildLengthPolicyUpdate(Guid id) => new()
        {
            Id = id,
            Template = "length check",
            Status = SensorStatus.Error,
            Destination = new PolicyDestinationUpdate(),
            Conditions =
            [
                new PolicyConditionUpdate(
                    PolicyOperation.GreaterThan,
                    PolicyProperty.Length,
                    new TargetValue(TargetType.Const, "3"))
            ],
        };

        private SensorUpdate BuildSensorUpdate(Guid sensorId, params PolicyUpdate[] policies) => new()
        {
            Id = sensorId,
            Policies = [.. policies],
            Initiator = InitiatorInfo.AsUser("test"),
        };

        private SensorEntity GetPersistedSensorEntity(BaseSensorModel sensor) =>
            _databaseCoreManager.DatabaseCore.GetAllSensors().Single(e => e.Id == sensor.Id.ToString());


        [Fact]
        [Trait("Category", "Sensor update")]
        public async Task UpdateSensor_ThrowingEvaluator_AnswersOkAndPersistsPolicy()
        {
            const string path = "evalErrorUpdate";
            var sensor = await CreateNullStringSensor(path);

            var policyId = Guid.NewGuid();

            // Pre-#1563: the evaluator exception escaped TryUpdateSensor's
            // catch-all, the answer was an error, and the DB write was
            // skipped while the in-memory apply had already happened.
            var result = await _valuesCache.UpdateSensorAsync(BuildSensorUpdate(sensor.Id, BuildLengthPolicyUpdate(policyId)));

            // SOFT failure contract: applied AND persisted.
            Assert.True(result.IsOk, result.Error);

            // The policy lives in memory, marked failed through the sensor's
            // error status naming it.
            Assert.Single(sensor.Policies, p => p.Id == policyId);
            Assert.True(sensor.Policies.SensorResult.HasError);
            Assert.Contains(policyId.ToString(), sensor.Policies.SensorResult.Message);

            // The DB row was written AFTER the apply: the persisted entity
            // carries the policy id (the sensor-creation write could not).
            Assert.Contains(policyId.ToString(), GetPersistedSensorEntity(sensor).Policies);
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task AddTemplate_ThrowingEvaluator_WarnsEvaluationFailed_PolicyStillApplied()
        {
            const string path = "evalErrorTemplate";
            var sensor = await CreateNullStringSensor(path);

            var template = BuildStringTemplate([$"*/{path}"], Guid.NewGuid(), PolicyOperation.GreaterThan,
                PolicyProperty.Length, new TargetValue(TargetType.Const, "3"));

            (bool Success, string Error, List<TemplateApplyWarning> Warnings) result = default;

            var records = await CaptureJournalAsync(async () =>
                result = await _valuesCache.AddAlertTemplateAsync(template));

            var (success, error, warnings) = result;

            // The save PERSISTED — an evaluation failure is soft and must not
            // fail the apply (#1396 split).
            Assert.True(success, error);

            // The evaluation failure reaches the CALLER as a warning naming
            // the policy — the review's P2: before the fix the template path
            // logged "partially applied" at Error (wrong — every policy was
            // applied) and the caller heard nothing.
            var warning = Assert.Single(warnings);
            Assert.Contains(path, warning.SensorPath);
            Assert.Contains("Failed to evaluate alert", warning.Reason);

            // The journal record describes the ACTUAL outcome — the evaluator
            // threw; the policy was applied, not skipped. (The apply's own
            // data-policy ADD record also lands in the journal — the
            // "evaluation failed" verdict is the distinguishing text.)
            var record = Assert.Single(records, r => r.PropertyName == "Alert"
                                                     && r.Key.Id == sensor.Id
                                                     && r.Path == sensor.FullPath
                                                     && r.NewValue.Contains("evaluation failed"));
            Assert.DoesNotContain("not applied", record.NewValue);
            Assert.Contains(warning.Reason, record.NewValue);

            // And the apply was real: the policy lives on the sensor and in
            // the DB row (a REJECTED policy would exist nowhere).
            var applied = Assert.Single(sensor.Policies, p => p.TemplateId == template.Id);
            Assert.Contains(applied.Id.ToString(), GetPersistedSensorEntity(sensor).Policies);
        }


        [Fact]
        [Trait("Category", "Template application")]
        public async Task AddTemplate_AfterHistoryClear_NoStaleEvaluationWarning()
        {
            const string path = "evalErrorStaleLeak";
            var sensor = await CreateNullStringSensor(path);

            // A user update attaches the broken policy; its revalidation on
            // the null last value throws and the failure line is recorded
            // (soft — the update answers Ok).
            var attach = BuildSensorUpdate(sensor.Id, BuildLengthPolicyUpdate(Guid.NewGuid()));
            Assert.True((await _valuesCache.UpdateSensorAsync(attach)).IsOk);

            // History clear: the last value is gone, so every later update
            // skips the revalidation — a recorded evaluation line must not
            // outlive the history it belonged to.
            await _valuesCache.ClearSensorHistoryAsync(new ClearHistoryRequest(sensor.Id));
            Assert.Null(sensor.LastValue);

            // A HEALTHY template apply on the same sensor must answer clean:
            // the stale line used to ride the aggregate error into the
            // warnings channel, naming the broken policy on an apply it had
            // nothing to do with.
            var template = BuildStringTemplate([$"*/{path}"], Guid.NewGuid(), PolicyOperation.Equal,
                PolicyProperty.Value, new TargetValue(TargetType.Const, "anything"));

            var (success, error, warnings) = await _valuesCache.AddAlertTemplateAsync(template);

            Assert.True(success, error);
            Assert.Empty(warnings);
            Assert.Contains(sensor.Policies, p => p.TemplateId == template.Id);
        }


        // A concrete String template carrying ONE condition — the #1401
        // suite's planting recipe: the condition is planted directly on the
        // carrier policy, and ApplyTemplateToSensor copies it verbatim into
        // the per-sensor update, where the String sensor's own policy type
        // rebuilds it. The planted condition class must match the PROPERTY's
        // value type exactly as the sensor side builds it (TypedPolicy):
        // Length is integer-typed, Value is string-typed. With
        // (GreaterThan, Length) the policy constructs cleanly and throws at
        // EVALUATION time on a null last value (#1563's trigger); with
        // (Equal, Value) it builds and evaluates cleanly.
        private AlertTemplateModel BuildStringTemplate(List<string> paths, Guid policyId,
            PolicyOperation operation, PolicyProperty property, TargetValue target)
        {
            var policy = Policy.BuildPolicy((byte)SensorType.String);

            policy.UpdatePolicy(new PolicyUpdate { Id = policyId, ConfirmationPeriod = 0 });

            PolicyCondition condition = property == PolicyProperty.Length
                ? new PolicyIntegerCondition<StringValue>()
                : new PolicyStringCondition<StringValue>();

            condition.Operation = operation;
            condition.Property = property;
            condition.Target = target;

            policy.Conditions.Add(condition);

            var template = new AlertTemplateModel
            {
                Name = $"Evaluation-error template {Guid.NewGuid():N}",
                FolderId = _fixture.FolderId,
                SensorType = (byte)SensorType.String,
                Paths = paths,
                Policies = [policy],
            };
            template.TryApplyPathTemplates(out _);

            return template;
        }
    }
}
