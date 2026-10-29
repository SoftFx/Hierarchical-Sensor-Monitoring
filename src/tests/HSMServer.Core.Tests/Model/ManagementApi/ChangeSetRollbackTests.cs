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

            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(icon: "🔥"), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            var rolledBack = await _changeSets.RollbackAsync(createdId, User);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Equal("completed", rolledBack.Value.Status);

            // The sensor is back to the seeded single policy; the created
            // policy is gone.
            Assert.Equal("seeded", Assert.Single(sensor.Policies).Template);

            var stored = _journal.Get(createdId);

            Assert.Equal(ChangeSetStatus.RolledBack, stored.Status);
        }

        [Fact]
        public async Task Rollback_RefusesOnContentChange_WithPerEntityDiff()
        {
            var sensor = await CreateSensorAsync("rbConflict");
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            // A web-editor-style intervening change on the same node: another
            // full-list write (content change, not a timestamp).
            var interveningId = Guid.NewGuid();
            var intervening = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = interveningId });
            Assert.True(intervening.Success, intervening.Failure?.Message);

            var refused = await _changeSets.RollbackAsync(createdId, User);

            Assert.False(refused.Success);
            Assert.Equal(PolicyWriteOutcome.Conflict, refused.Failure.Outcome);
            Assert.NotNull(refused.Failure.Details);
            Assert.Contains(refused.Failure.Details.Values, v => v.Contains("added after the journaled write"));

            // Nothing was restored.
            Assert.Equal(2, sensor.Policies.Count());
            Assert.Equal(ChangeSetStatus.Applied, _journal.Get(createdId).Status);
        }

        [Fact]
        public async Task Rollback_TwoConflictingEntriesUnderOnePath_ReportTheMergedDiff()
        {
            // One change set, two entries with the SAME path (the sensor's
            // data and TTL lists journaled as separate writes — story 1's
            // grouped plan): the per-entity diff report merges both entries'
            // conflicts under the one path instead of tripping the dictionary
            // on a duplicate key.
            var sensor = await CreateSensorAsync("rbTwoEntries");
            var changeSetId = Guid.NewGuid();

            var data = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            var ttl = await _policies.CreateSensorTtlPolicyAsync(sensor.Id, TtlPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(data.Success, data.Failure?.Message);
            Assert.True(ttl.Success, ttl.Failure?.Message);

            // Intervening writes on BOTH lists — both entries conflict.
            var interveningDataId = Guid.NewGuid();
            var interveningData = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = interveningDataId });
            var interveningTtlId = Guid.NewGuid();
            var interveningTtl = await _policies.CreateSensorTtlPolicyAsync(sensor.Id, TtlPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = interveningTtlId });
            Assert.True(interveningData.Success, interveningData.Failure?.Message);
            Assert.True(interveningTtl.Success, interveningTtl.Failure?.Message);

            var refused = await _changeSets.RollbackAsync(changeSetId, User);

            Assert.False(refused.Success);
            Assert.Equal(PolicyWriteOutcome.Conflict, refused.Failure.Outcome);

            var detail = Assert.Single(refused.Failure.Details);

            Assert.Equal(sensor.FullPath, detail.Key);
            Assert.Contains("added after the journaled write", detail.Value);
        }

        [Fact]
        public async Task Rollback_WebEditorStyleReAssertOfSameContent_IsNotAConflict()
        {
            var sensor = await CreateSensorAsync("rbReAssert");
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
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

            var rolledBack = await _changeSets.RollbackAsync(createdId, User);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Empty(sensor.Policies);
        }

        [Fact]
        public async Task Rollback_ForceOverridesConflicts()
        {
            var sensor = await CreateSensorAsync("rbForce");
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            var interveningId = Guid.NewGuid();
            var intervening = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = interveningId });
            Assert.True(intervening.Success, intervening.Failure?.Message);

            var forced = await _changeSets.RollbackAsync(createdId, User, force: true);

            Assert.True(forced.Success, forced.Failure?.Message);

            // Forced restore replaces the list with the recorded before-image
            // (empty), clobbering the intervening write deliberately.
            Assert.Empty(sensor.Policies);
            Assert.Equal(ChangeSetStatus.RolledBack, _journal.Get(createdId).Status);
        }

        [Fact]
        public async Task RollbackDryRun_ReportsPlanAndConflicts_AppliesNothing()
        {
            var sensor = await CreateSensorAsync("rbDryRun");
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            var preview = await _changeSets.RollbackAsync(createdId, User, dryRun: true);

            Assert.True(preview.Success, preview.Failure?.Message);
            Assert.True(preview.Value.DryRun);
            Assert.Equal("completed", preview.Value.Status);
            Assert.Equal("wouldRestore", Assert.Single(preview.Value.Entries).Status);

            // Nothing applied, nothing marked restored.
            Assert.Single(sensor.Policies);
            Assert.Null(_journal.Get(createdId).Entries[0].RestoredBy);
        }

        [Fact]
        public async Task Rollback_IsJournaled_RedoViaRollbackOfRollback()
        {
            var sensor = await CreateSensorAsync("rbRedo");
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(icon: "redo-me"), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            var rolledBack = await _changeSets.RollbackAsync(createdId, User);
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
            var refused = await _changeSets.RollbackAsync(createdId, User);

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

            var deletedId = Guid.NewGuid();
            var deleted = await _templates.DeleteTemplateAsync(templateId, User,
                new ChangeSetWriteOptions { ChangeSetId = deletedId });
            Assert.True(deleted.Success, deleted.Failure?.Message);
            Assert.Empty(sensor.Policies);

            var rolledBack = await _changeSets.RollbackAsync(deletedId, User);

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

            var withReferenceId = Guid.NewGuid();
            var withReference = await _policies.CreateSensorPolicyAsync(sensor.Id,
                DataPolicy(scheduleId: schedule.Value.Id), User,
                new ChangeSetWriteOptions { ChangeSetId = withReferenceId });
            Assert.True(withReference.Success, withReference.Failure?.Message);

            var deletedId = Guid.NewGuid();
            var deleted = await _schedules.DeleteScheduleAsync(schedule.Value.Id, User,
                cancellationToken: default, new ChangeSetWriteOptions { ChangeSetId = deletedId });
            Assert.True(deleted.Success, deleted.Failure?.Message);

            // The delete detached the reference; the policy survives without
            // it.
            Assert.Null(sensor.Policies.Single().ScheduleId);

            var rolledBack = await _changeSets.RollbackAsync(deletedId, User);

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
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            var list = _changeSets.ListChangeSets(User,
                new ChangeSetFilter { EntityId = sensor.Id });

            Assert.True(list.Success);
            var summary = Assert.Single(list.Value);

            Assert.Equal(createdId.ToString(), summary.Id);
            Assert.Equal("api-owner", summary.Author);
            Assert.Equal("applied", summary.Status);

            var details = _changeSets.GetChangeSet(createdId, User);

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
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            await _valuesCache.RemoveSensorAsync(sensor.Id, Force);

            // The single-entry change set does not outlive its sensor.
            Assert.Null(_journal.Get(createdId));
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

        [Fact]
        public void CookieUserAccess_ProductTtlEntry_OfASubProduct_ResolvesTheRecordedRoot()
        {
            // A ProductTtl entry's entity may be a SUB-product, but a web
            // user's product roles are granted on ROOT products — the cookie
            // twin must check the recorded root (entry.ParentId), like the
            // sensor arm, or the entry is invisible to the root's viewer.
            var root = Guid.NewGuid();
            var user = new User(new HSMDatabase.AccessManager.DatabaseEntities.UserEntity
            {
                UserName = "viewer",
                ProductsRoles = [new(root.ToString(), (byte)ProductRoleEnum.ProductManager)],
            });

            var access = new CookieUserAccess(user, _valuesCache);
            var entry = new ChangeSetEntry
            {
                Kind = ChangeSetEntryKind.ProductTtlPolicies,
                EntityId = Guid.NewGuid(),
                ParentId = root,
            };

            Assert.True(access.CanRead(entry));
            Assert.True(access.CanWrite(entry));
        }

        [Fact]
        public async Task Rollback_FromACookiePrincipal_RestoresEntries_AndJournalsTheWebUser()
        {
            // The web UI rolls back with a cookie principal — no HsmApiToken
            // claims. The compensating writes must authorize through the
            // supplied access (the token evaluator would answer NotFound for
            // every node), and the rollback's journal author is the login
            // name, not "api-token".
            var sensor = await CreateSensorAsync("rbCookie");
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(icon: "cookie-me"), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            var cookie = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.Name, "web-admin")], "Cookies"));
            var access = new CookieUserAccess(new User("web-admin") { IsAdmin = true }, _valuesCache);

            var rolledBack = await _changeSets.RollbackAsync(createdId, cookie, access: access);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Equal("completed", rolledBack.Value.Status);
            Assert.Empty(sensor.Policies);

            Assert.Equal("web-admin", _journal.Get(Guid.Parse(rolledBack.Value.RollbackChangeSetId)).Author);
        }

        [Fact]
        public async Task TwoWritesToOneList_UnderOneChangeSet_RollBackCleanlyWithoutForce()
        {
            // A grouped plan may write the same list twice: the set's own
            // later write replaces the earlier entry's after-image, so an
            // earlier entry compares against the NEXT write's before-image —
            // only the latest write on a target compares against live state.
            // Without that chain rule every grouped edit would demand force.
            var sensor = await CreateSensorAsync("rbTwice");
            var changeSetId = Guid.NewGuid();

            var first = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            var second = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(first.Success, first.Failure?.Message);
            Assert.True(second.Success, second.Failure?.Message);
            Assert.Equal(2, sensor.Policies.Count());

            var rolledBack = await _changeSets.RollbackAsync(changeSetId, User);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Equal("completed", rolledBack.Value.Status);
            Assert.Empty(sensor.Policies);
        }

        [Fact]
        public async Task Rollback_PreFlightDeniesAFanOutNodeTheCallerCannotWrite()
        {
            // The entry-level boundary alone is not the whole write: the
            // restores re-authorize per node, so a caller who cannot write
            // one fan-out node is refused UP FRONT instead of applying half
            // a fan-out before the per-node authorization fails.
            var first = await CreateSensorAsync("rbFanA");
            var second = await CreateSensorAsync("rbFanB");
            var changeSetId = Guid.NewGuid();

            var created = await _templates.CreateTemplateAsync(User, TemplateDto("rbFanA", "rbFanB"),
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            var access = new SelectiveAccess(canWriteNode: image => image.NodeId != second.Id);

            var refused = await _changeSets.RollbackAsync(changeSetId, User, access: access);

            Assert.False(refused.Success);
            Assert.Equal(PolicyWriteOutcome.Forbidden, refused.Failure.Outcome);

            // Nothing restored: the template and both fan-out applies stand.
            Assert.NotNull(_valuesCache.GetAlertTemplate(created.Value.Id));
            Assert.Single(first.Policies);
            Assert.Single(second.Policies);
        }

        [Fact]
        public async Task FanOutRestoreFailure_JournalsTheAlreadyRestoredHalf()
        {
            // A node restore that fails mid-loop (here: denied at restore
            // time after passing the pre-flight) must leave an undo point for
            // everything the attempt already applied — the rollback's own
            // journal entry carries the restored nodes, so a retry's redo
            // parity survives.
            var first = await CreateSensorAsync("rbHalfA");
            var second = await CreateSensorAsync("rbHalfB");
            var changeSetId = Guid.NewGuid();

            var created = await _templates.CreateTemplateAsync(User, TemplateDto("rbHalfA", "rbHalfB"),
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            // Deny the LAST node of the recorded fan-out at RESTORE time
            // only (the pre-flight allows every node).
            var deniedNode = _journal.Get(changeSetId).Entries.Single().Fanout.Last();
            var access = new SelectiveAccess(canWriteResource: resource =>
                resource.Kind != ApiTokenResourceKind.Sensor || resource.Id != deniedNode.NodeId);

            var result = await _changeSets.RollbackAsync(changeSetId, User, access: access);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal("partial", result.Value.Status);

            var redoEntry = Assert.Single(_journal.Get(Guid.Parse(result.Value.RollbackChangeSetId)).Entries);

            // Exactly the nodes restored before the denial ride the rollback
            // entry. (The row compensation itself strips the template's
            // policies from every matching sensor — including the denied
            // node; that change rides the row's image, not a node image.)
            var restoredNode = Assert.Single(redoEntry.Fanout ?? []);

            Assert.NotEqual(deniedNode.NodeId, restoredNode.NodeId);
            Assert.Empty(_valuesCache.GetSensor(restoredNode.NodeId).Policies);
            Assert.Empty(_valuesCache.GetSensor(deniedNode.NodeId).Policies);
            Assert.Contains(result.Value.Entries, report => report.Status == "failed");

            // The template row itself was compensated (its create undone).
            Assert.Null(_valuesCache.GetAlertTemplate(created.Value.Id));
        }

        [Fact]
        public async Task ForeignAuthorWrite_JournalsUnderItsOwnRequestGroup()
        {
            // #1588: group ids are SERVER-minted per request, so the #1570
            // fork rule is structurally gone — every request journals under
            // exactly its own group, and neither set blocks the other's
            // rollback (the other's write is an intervening change the
            // conflict check reports).
            var users = new Mock<IUserManager>();
            users.Setup(u => u[It.IsAny<Guid>()]).Returns((Guid id) => new User($"owner-{id:N}"));

            var policies = new PolicyAdministrationService(_valuesCache, _authorization.Object, users.Object,
                _chats.Object, _folders.Object, _alertScheduleProvider,
                ChangeSetTestHelpers.Recorder(_databaseCoreManager.DatabaseCore));

            var sensor = await CreateSensorAsync("rbFork");
            var mineId = Guid.NewGuid();
            var strangerId = Guid.NewGuid();
            var stranger = BuildPrincipal();

            var mine = await policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = mineId });
            Assert.True(mine.Success, mine.Failure?.Message);

            var theirs = await policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), stranger,
                new ChangeSetWriteOptions { ChangeSetId = strangerId });
            Assert.True(theirs.Success, theirs.Failure?.Message);

            // One entry in each group; the author's set still rolls back
            // (force over the intervening change's diff, no 403 block).
            Assert.Single(_journal.Get(mineId).Entries);
            Assert.Single(_journal.Get(strangerId).Entries);

            var rolledBack = await _changeSets.RollbackAsync(mineId, User, force: true);
            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
        }

        [Fact]
        public async Task Rollback_WithNothingJournaled_OmitsTheRedoHandle()
        {
            // The write echoes' own rule, applied to the rollback itself: the
            // response carries a redo handle ONLY when at least one rollback
            // entry actually landed in the journal — a mid-restore failure
            // that journals nothing answers without one.
            var sensor = await CreateSensorAsync("rbNoRedo");
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            // The pre-flight passes (the entry-level check allows), but every
            // compensating write is denied at restore time — nothing restores,
            // nothing journals.
            var access = new SelectiveAccess(canWriteResource: _ => false);

            var result = await _changeSets.RollbackAsync(createdId, User, access: access);

            Assert.True(result.Success, result.Failure?.Message);
            Assert.Equal("partial", result.Value.Status);
            Assert.Null(result.Value.RollbackChangeSetId);
            Assert.Contains(result.Value.Entries, report => report.Status == "failed");
        }

        [Fact]
        public async Task ConcurrentRollbacks_OfOneChangeSet_Serialize_ExactlyOneWins()
        {
            // The per-id rollback gate: the second rollback of a concurrent
            // pair finds the set RolledBack and answers a conflict, instead
            // of replaying every entry and journaling an orphan rollback
            // change set whose redo would re-apply writes nobody intended.
            var sensor = await CreateSensorAsync("rbRace");
            var createdId = Guid.NewGuid();
            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), User,
                new ChangeSetWriteOptions { ChangeSetId = createdId });
            Assert.True(created.Success, created.Failure?.Message);

            var first = Task.Run(() => _changeSets.RollbackAsync(createdId, User));
            var second = Task.Run(() => _changeSets.RollbackAsync(createdId, User));

            await Task.WhenAll(first, second);

            var outcomes = new[] { first.Result, second.Result }.OrderBy(result => result.Success).ToList();

            Assert.True(outcomes[1].Success, outcomes[1].Failure?.Message);
            Assert.False(outcomes[0].Success);
            Assert.Equal(PolicyWriteOutcome.Conflict, outcomes[0].Failure.Outcome);
            Assert.Contains("already rolled back", outcomes[0].Failure.Message);
        }


        // === Test vocabulary (mirrors ChangeSetJournalingTests) ===

        // A permissive IChangeSetAccess for the rollback tests: everything
        // allowed except the one predicate the test denies (a fan-out node
        // or a resource-level write).
        private sealed class SelectiveAccess : IChangeSetAccess
        {
            private readonly Func<NodePolicyImage, bool> _canWriteNode;
            private readonly Func<ApiTokenResource, bool> _canWriteResource;

            public SelectiveAccess(Func<NodePolicyImage, bool> canWriteNode = null,
                Func<ApiTokenResource, bool> canWriteResource = null)
            {
                _canWriteNode = canWriteNode;
                _canWriteResource = canWriteResource;
            }

            public bool CanRead(ChangeSetEntry entry) => true;

            public bool CanWrite(ChangeSetEntry entry) => true;

            public bool CanReadNode(NodePolicyImage image) => true;

            public bool CanWriteNode(NodePolicyImage image) => _canWriteNode?.Invoke(image) ?? true;

            public bool CanWrite(ApiTokenResource resource) => _canWriteResource?.Invoke(resource) ?? true;
        }

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

        private static TtlPolicyDto TtlPolicy() => new()
        {
            Interval = "00:30:00",
            Notification = new AlertNotificationDto { Template = "api ttl [$product]$path" },
            Destination = new AlertDestinationDto { Mode = "FromParent" },
        };

        private AlertTemplateDto TemplateDto(params string[] sensorNames) => new()
        {
            Name = $"template-{Guid.NewGuid():N}",
            SensorType = (byte)SensorType.Integer,
            FolderId = _fixture.FolderId,
            Paths = [.. sensorNames.Select(name => $"*/{name}")],
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
