using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMCommon.TaskResult;
using HSMServer.Authentication;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Notifications.Chats;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // Rollback semantics of the undo journal (#1570) at the service seam —
    // apply, journal, roll back, observe state — on the real cache and the
    // real test LevelDB: clean rollback, conflict detection by CONTENT (a
    // web-editor-style full-list re-assert must not false-positive), force
    // override, dry-run plans, redo via rollback of the rollback, and the
    // template fan-out restore.
    [Collection("Database collection")]
    public class ChangeSetRollbackTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private static readonly ClaimsPrincipal User = BuildPrincipal();

        private readonly TemplateConcurrencyFixture _fixture;

        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IFolderManager> _folders = new();

        private readonly PolicyAdministrationService _policies;
        private readonly AlertTemplateAdministrationService _templates;
        private readonly AlertScheduleAdministrationService _schedules;
        private readonly ChangeSetAdministrationService _changeSets;
        private readonly IChangeSetJournal _journal;


        public ChangeSetRollbackTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(new User("api-owner"));
            _chats.Setup(c => c.GetValues()).Returns([]);

            var recorder = ChangeSetTestHelpers.Recorder(_databaseCoreManager.DatabaseCore);
            _journal = new ChangeSetJournal(_databaseCoreManager.DatabaseCore);

            _policies = new PolicyAdministrationService(_valuesCache, _authorization.Object, _users.Object,
                _chats.Object, _folders.Object, _alertScheduleProvider, recorder);

            _templates = new AlertTemplateAdministrationService(_valuesCache, _folders.Object, _chats.Object,
                _alertScheduleProvider, _authorization.Object, _users.Object, recorder,
                NullLogger<AlertTemplateAdministrationService>.Instance);

            var reader = new AlertReadService(_valuesCache, _alertScheduleProvider, _authorization.Object);
            _schedules = new AlertScheduleAdministrationService(_alertScheduleProvider, _valuesCache,
                _authorization.Object, reader, recorder, _users.Object);

            _changeSets = new ChangeSetAdministrationService(_journal, _valuesCache, _alertScheduleProvider,
                _authorization.Object, _users.Object, _policies, _templates, _schedules, recorder);
        }


        [Fact]
        public async Task CleanRollback_RestoresTheBeforeState()
        {
            var sensor = await CreateSensorAsync("rbClean");
            var seeded = await SeedPoliciesAsync(sensor, RegularUpdate("seeded"));
            Assert.True(seeded.IsOk, seeded.Error);

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(icon: "🔥"), User);
            Assert.True(created.Success, created.Failure?.Message);

            var rolledBack = await _changeSets.RollbackAsync(created.ChangeSetId.Value, User);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Equal("completed", rolledBack.Value.Status);

            // The sensor is back to the seeded single policy; the created
            // policy is gone.
            Assert.Equal("seeded", Assert.Single(sensor.Policies).Template);

            var stored = _journal.Get(created.ChangeSetId.Value);

            Assert.Equal(ChangeSetStatus.RolledBack, stored.Status);
        }

        [Fact]
        public async Task Rollback_RefusesOnContentChange_WithPerEntityDiff()
        {
            var sensor = await CreateSensorAsync("rbConflict");
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);
            Assert.True(created.Success, created.Failure?.Message);

            // A web-editor-style intervening change on the same node: another
            // full-list write (content change, not a timestamp).
            var intervening = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);
            Assert.True(intervening.Success, intervening.Failure?.Message);

            var refused = await _changeSets.RollbackAsync(created.ChangeSetId.Value, User);

            Assert.False(refused.Success);
            Assert.Equal(PolicyWriteOutcome.Conflict, refused.Failure.Outcome);
            Assert.NotNull(refused.Failure.Details);
            Assert.Contains(refused.Failure.Details.Values, v => v.Contains("added after the journaled write"));

            // Nothing was restored.
            Assert.Equal(2, sensor.Policies.Count());
            Assert.Equal(ChangeSetStatus.Applied, _journal.Get(created.ChangeSetId.Value).Status);
        }

        [Fact]
        public async Task Rollback_WebEditorStyleReAssertOfSameContent_IsNotAConflict()
        {
            var sensor = await CreateSensorAsync("rbReAssert");
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);
            Assert.True(created.Success, created.Failure?.Message);

            // The web editor re-asserts the FULL list on every form save —
            // identical content, new write. The rollback must not
            // false-positive on it (the #1570 content-comparison rule).
            var reAssert = await _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = InitiatorInfo.AsSystemForce("web-editor-save"),
                Policies = [.. sensor.Policies.Select(p => new PolicyUpdate(p, InitiatorInfo.AsSystemForce("web-editor-save")))],
            });
            Assert.True(reAssert.IsOk, reAssert.Error);

            var rolledBack = await _changeSets.RollbackAsync(created.ChangeSetId.Value, User);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Empty(sensor.Policies);
        }

        [Fact]
        public async Task Rollback_ForceOverridesConflicts()
        {
            var sensor = await CreateSensorAsync("rbForce");
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);
            Assert.True(created.Success, created.Failure?.Message);

            var intervening = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);
            Assert.True(intervening.Success, intervening.Failure?.Message);

            var forced = await _changeSets.RollbackAsync(created.ChangeSetId.Value, User, force: true);

            Assert.True(forced.Success, forced.Failure?.Message);

            // Forced restore replaces the list with the recorded before-image
            // (empty), clobbering the intervening write deliberately.
            Assert.Empty(sensor.Policies);
            Assert.Equal(ChangeSetStatus.RolledBack, _journal.Get(created.ChangeSetId.Value).Status);
        }

        [Fact]
        public async Task RollbackDryRun_ReportsPlanAndConflicts_AppliesNothing()
        {
            var sensor = await CreateSensorAsync("rbDryRun");
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);
            Assert.True(created.Success, created.Failure?.Message);

            var preview = await _changeSets.RollbackAsync(created.ChangeSetId.Value, User, dryRun: true);

            Assert.True(preview.Success, preview.Failure?.Message);
            Assert.True(preview.Value.DryRun);
            Assert.Equal("completed", preview.Value.Status);
            Assert.Equal("wouldRestore", Assert.Single(preview.Value.Entries).Status);

            // Nothing applied, nothing marked restored.
            Assert.Single(sensor.Policies);
            Assert.Null(_journal.Get(created.ChangeSetId.Value).Entries[0].RestoredBy);
        }

        [Fact]
        public async Task Rollback_IsJournaled_RedoViaRollbackOfRollback()
        {
            var sensor = await CreateSensorAsync("rbRedo");
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(icon: "redo-me"), User);
            Assert.True(created.Success, created.Failure?.Message);

            var rolledBack = await _changeSets.RollbackAsync(created.ChangeSetId.Value, User);
            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Empty(sensor.Policies);

            // The rollback change set exists and is applied.
            var redoId = Guid.Parse(rolledBack.Value.RollbackChangeSetId);
            var redoSet = _journal.Get(redoId);

            Assert.NotNull(redoSet);
            Assert.Equal(ChangeSetStatus.Applied, redoSet.Status);
            Assert.Single(redoSet.Entries);
            Assert.Equal(ChangeSetOperation.Delete, redoSet.Entries[0].Operation);

            // Redo: roll back the rollback — the policy returns.
            var redone = await _changeSets.RollbackAsync(redoId, User);

            Assert.True(redone.Success, redone.Failure?.Message);
            Assert.Equal("redo-me", Assert.Single(sensor.Policies).Icon);

            // And the original set is terminal — no double undo.
            var refused = await _changeSets.RollbackAsync(created.ChangeSetId.Value, User);

            Assert.False(refused.Success);
            Assert.Equal(PolicyWriteOutcome.Conflict, refused.Failure.Outcome);
        }

        [Fact]
        public async Task TemplateRollback_RestoresTemplateRowAndEverySensorList()
        {
            var sensor = await CreateSensorAsync("rbTemplate");
            var changeSetId = Guid.NewGuid();

            var created = await _templates.CreateTemplateAsync(User, TemplateDto("rbTemplate"),
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            // The fan-out materialized template policies on the sensor.
            Assert.Contains(sensor.Policies, p => p.TemplateId == created.Value.Id);

            var rolledBack = await _changeSets.RollbackAsync(changeSetId, User);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);

            // The template is gone and the sensor's lists are empty again.
            Assert.Null(_valuesCache.GetAlertTemplate(created.Value.Id));
            Assert.Empty(sensor.Policies);
            Assert.Empty(sensor.Policies.TTLPolicies);
        }

        [Fact]
        public async Task TemplateDeleteRollback_RecreatesTemplateAndPolicies()
        {
            var sensor = await CreateSensorAsync("rbTemplateDelete");

            var created = await _templates.CreateTemplateAsync(User, TemplateDto("rbTemplateDelete"));
            Assert.True(created.Success, created.Failure?.Message);
            var templateId = created.Value.Id;

            var deleted = await _templates.DeleteTemplateAsync(templateId, User);
            Assert.True(deleted.Success, deleted.Failure?.Message);
            Assert.Empty(sensor.Policies);

            var rolledBack = await _changeSets.RollbackAsync(deleted.ChangeSetId.Value, User);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);

            // The template row and its sensor policies are back under the
            // SAME id.
            Assert.NotNull(_valuesCache.GetAlertTemplate(templateId));
            Assert.Contains(sensor.Policies, p => p.TemplateId == templateId);
        }

        [Fact]
        public async Task ScheduleDeleteRollback_RestoresScheduleAndReferences()
        {
            var sensor = await CreateSensorAsync("rbSchedule");

            // A sensor policy that references the schedule.
            var schedule = await _schedules.CreateScheduleAsync(User, ScheduleDto());
            Assert.True(schedule.Success, schedule.Failure?.Message);

            var withReference = await _policies.CreateSensorPolicyAsync(sensor.Id,
                DataPolicy(scheduleId: schedule.Value.Id), User);
            Assert.True(withReference.Success, withReference.Failure?.Message);

            var deleted = await _schedules.DeleteScheduleAsync(schedule.Value.Id, User);
            Assert.True(deleted.Success, deleted.Failure?.Message);

            // The delete detached the reference; the policy survives without
            // it.
            Assert.Null(sensor.Policies.Single().ScheduleId);

            var rolledBack = await _changeSets.RollbackAsync(deleted.ChangeSetId.Value, User);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);

            // The schedule row is back AND the policy's schedule reference
            // with it.
            Assert.NotNull(_alertScheduleProvider.GetSchedule(schedule.Value.Id));
            Assert.Equal(schedule.Value.Id, sensor.Policies.Single().ScheduleId);
        }

        [Fact]
        public async Task ListAndGet_FilterByCallerSightAndEntity()
        {
            var sensor = await CreateSensorAsync("rbList");
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);
            Assert.True(created.Success, created.Failure?.Message);

            var list = _changeSets.ListChangeSets(User,
                new ChangeSetFilter { EntityId = sensor.Id });

            Assert.True(list.Success);
            var summary = Assert.Single(list.Value);

            Assert.Equal(created.ChangeSetId.Value.ToString(), summary.Id);
            Assert.Equal("api-owner", summary.Author);
            Assert.Equal("applied", summary.Status);

            var details = _changeSets.GetChangeSet(created.ChangeSetId.Value, User);

            Assert.True(details.Success);
            Assert.Single(details.Value.Entries);
        }

        [Fact]
        public async Task GetChangeSet_UnknownId_Is404()
        {
            var result = _changeSets.GetChangeSet(Guid.NewGuid(), User);

            Assert.False(result.Success);
            Assert.Equal(PolicyWriteOutcome.NotFound, result.Failure.Outcome);
        }

        [Fact]
        public async Task SensorDeletion_PurgesEntries_EntrylessChangeSetIsRemoved()
        {
            var sensor = await CreateSensorAsync("rbPurgeSolo");
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);
            Assert.True(created.Success, created.Failure?.Message);

            await _valuesCache.RemoveSensorAsync(sensor.Id, Force);

            // The single-entry change set does not outlive its sensor.
            Assert.Null(_journal.Get(created.ChangeSetId.Value));
        }

        [Fact]
        public async Task SensorDeletion_MultiEntryChangeSet_GoesPartialAndRollsBackSurvivors()
        {
            var sensorA = await CreateSensorAsync("rbPurgeA");
            var sensorB = await CreateSensorAsync("rbPurgeB");
            var changeSetId = Guid.NewGuid();

            var first = await _policies.CreateSensorPolicyAsync(sensorA.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            var second = await _policies.CreateSensorPolicyAsync(sensorB.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(first.Success, first.Failure?.Message);
            Assert.True(second.Success, second.Failure?.Message);

            await _valuesCache.RemoveSensorAsync(sensorA.Id, Force);

            var partialSet = _journal.Get(changeSetId);

            Assert.Equal(ChangeSetStatus.Partial, partialSet.Status);
            Assert.Single(partialSet.Entries); // the deleted sensor's entry is gone

            // The surviving entry still rolls back.
            var rolledBack = await _changeSets.RollbackAsync(changeSetId, User);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Empty(sensorB.Policies);
        }

        [Fact]
        public async Task Rollback_VersusConcurrentWriteToTheSameNode_LeavesConsistentState()
        {
            // The #1570 concurrency pin: the rollback's full-list restore and
            // a concurrent policy write to the SAME node both go through the
            // write gates, so the final state is one of the two consistent
            // outcomes — the sensor's list is never a torn mix. Bounded
            // iterations; the gate, not timing, is the subject.
            for (var i = 0; i < 5; i++)
            {
                var sensor = await CreateSensorAsync($"rbConcurrent{i}");
                var changeSetId = Guid.NewGuid();

                var journaled = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                    new ChangeSetWriteOptions { ChangeSetId = changeSetId });
                Assert.True(journaled.Success, journaled.Failure?.Message);

                var rollback = _changeSets.RollbackAsync(changeSetId, User);
                var concurrent = _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User);

                await Task.WhenAll(rollback, concurrent);

                // Either the concurrent write won (rollback conflicted or ran
                // after it) or the rollback did — the list holds 0 or 1
                // well-formed policies either way, never a duplicate of the
                // journaled policy id twice.
                var ids = sensor.Policies.Select(p => p.Id).ToList();

                Assert.True(ids.Count <= 2, $"torn state: {ids.Count} policies");
                Assert.Equal(ids.Count, ids.Distinct().Count());
            }
        }


        // === Test vocabulary (mirrors ChangeSetJournalingTests) ===

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private static InitiatorInfo Force { get; } = InitiatorInfo.AsSystemForce("change_set_rollback_tests");

        private async Task<Core.Model.BaseSensorModel> CreateSensorAsync(string name)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, name, DateTime.UtcNow);
            var result = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            Assert.True(result.IsOk, result.Error);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, name, out var sensor));

            return sensor;
        }

        private Task<TaskResult> SeedPoliciesAsync(Core.Model.BaseSensorModel sensor, params PolicyUpdate[] policies) =>
            _valuesCache.UpdateSensorAsync(new SensorUpdate
            {
                Id = sensor.Id,
                Initiator = Force,
                Policies = [.. policies],
            });

        private static PolicyUpdate RegularUpdate(string template) => new()
        {
            Id = Guid.NewGuid(),
            Template = template,
            Icon = $"{template}-icon",
            Destination = new PolicyDestinationUpdate(),
            Conditions =
            [
                new PolicyConditionUpdate(PolicyOperation.GreaterThan, PolicyProperty.Value,
                    new TargetValue(TargetType.Const, "10")),
            ],
            Initiator = Force,
        };

        private static PolicyDto DataPolicy(string icon = null, Guid? scheduleId = null) => new()
        {
            Icon = icon,
            ScheduleId = scheduleId,
            Conditions =
            [
                new AlertConditionDto
                {
                    Property = "Value",
                    Operation = "GreaterThan",
                    Target = JsonSerializer.SerializeToElement(20),
                },
            ],
            Notification = new AlertNotificationDto { Template = "api [$product]$path" },
            Destination = new AlertDestinationDto { Mode = "FromParent" },
        };

        private AlertTemplateDto TemplateDto(string sensorName) => new()
        {
            Name = $"template-{Guid.NewGuid():N}",
            SensorType = (byte)SensorType.Integer,
            FolderId = _fixture.FolderId,
            Paths = [$"*/{sensorName}"],
            Policies =
            [
                new AlertPolicyDto
                {
                    Conditions =
                    [
                        new PolicyConditionDto
                        {
                            Target = new PolicyTargetDto { Type = (byte)TargetType.Const, Value = "42" },
                            Combination = (byte)PolicyCombination.And,
                            Operation = (byte)PolicyOperation.GreaterThan,
                            Property = (byte)PolicyProperty.Value,
                        },
                    ],
                    SensorStatus = (byte)SensorStatus.Ok,
                },
            ],
            TtlPolicies = [new AlertPolicyDto { SensorStatus = (byte)SensorStatus.Ok }],
            Ttls = [new TimeIntervalDto { Interval = (long)CoreTimeInterval.Ticks, Ticks = TimeSpan.FromMinutes(1).Ticks }],
        };

        private static AlertScheduleUpsertDto ScheduleDto() => new()
        {
            Name = $"schedule-{Guid.NewGuid():N}",
            Timezone = "UTC",
            Schedule = """
                daySchedules:
                  - days: [mon, tue, wed, thu, fri]
                    windows:
                      - start: 09:00
                        end: 18:00
                """,
        };
    }
}
