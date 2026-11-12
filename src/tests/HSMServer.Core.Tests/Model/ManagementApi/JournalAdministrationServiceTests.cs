using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache.UpdateEntities;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Journal;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.TableOfChanges;
using HSMServer.Core.Tests.Infrastructure;
using HSMServer.Core.Tests.MonitoringCoreTests;
using HSMServer.Core.Tests.MonitoringCoreTests.Fixture;
using HSMServer.Core.Tests.TreeValuesCacheTests.Fixture;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Model.ManagementApi.Journal;
using HSMServer.Model.Folders;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace HSMServer.Core.Tests.Model.ManagementApi
{
    // #1588: the unified Journal surface — every configuration write from
    // every source (REST, MCP and the web editor's single write path) leaves
    // a kind-stamped journal record linked to its request's undo group; the
    // journal service lists/filters them and rolls back BY RECORD, admin
    // only. The undo mechanics themselves stay covered by the change-set
    // suites; these tests pin the JOURNAL-side contract.
    [Collection("Database collection")]
    public class JournalAdministrationServiceTests : MonitoringCoreTestsBase<TemplateConcurrencyFixture>
    {
        private static readonly ClaimsPrincipal TokenUser = BuildPrincipal();

        private readonly TemplateConcurrencyFixture _fixture;

        private readonly Mock<IApiTokenAuthorizationService> _authorization = new();
        private readonly Mock<IUserManager> _users = new();
        private readonly Mock<IChatsManager> _chats = new();
        private readonly Mock<IFolderManager> _folders = new();

        private readonly User _owner = new("api-owner") { IsAdmin = true };

        private readonly PolicyAdministrationService _policies;
        private readonly AlertScheduleAdministrationService _schedules;
        private readonly AlertTemplateAdministrationService _templates;
        private readonly ChangeSetAdministrationService _engine;
        private readonly HSMServer.Core.ChangeSets.IChangeSetJournal _changeSetJournal;
        private readonly JournalAdministrationService _journal;
        private readonly JournalService _journalService;


        public JournalAdministrationServiceTests(TemplateConcurrencyFixture fixture, DatabaseRegisterFixture registerFixture)
            : base(fixture, registerFixture, addTestProduct: false)
        {
            _fixture = fixture;

            _authorization.Setup(a => a.AuthorizeWrite(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);
            _authorization.Setup(a => a.AuthorizeRead(It.IsAny<ClaimsPrincipal>(), It.IsAny<ApiTokenResource>()))
                .Returns(ApiTokenAuthorization.Allowed);

            _users.Setup(u => u[It.IsAny<Guid>()]).Returns(() => _owner);
            _chats.Setup(c => c.GetValues()).Returns([]);

            _changeSetJournal = new ChangeSetJournal(_databaseCoreManager.DatabaseCore);
            _journalService = new JournalService(_databaseCoreManager.DatabaseCore);
            var recorder = new ChangeSetRecorder(_changeSetJournal, _journalService,
                NullLogger<ChangeSetRecorder>.Instance);

            _policies = new PolicyAdministrationService(_valuesCache, _authorization.Object, _users.Object,
                _chats.Object, _folders.Object, _alertScheduleProvider, recorder);

            _templates = new AlertTemplateAdministrationService(_valuesCache, _folders.Object, _chats.Object,
                _alertScheduleProvider, _authorization.Object, _users.Object, recorder,
                NullLogger<AlertTemplateAdministrationService>.Instance);

            var reader = new AlertReadService(_valuesCache, _alertScheduleProvider, _authorization.Object);
            _schedules = new AlertScheduleAdministrationService(_alertScheduleProvider, _valuesCache,
                _authorization.Object, reader, recorder, _users.Object);

            _engine = new ChangeSetAdministrationService(_changeSetJournal, _valuesCache, _alertScheduleProvider,
                _authorization.Object, _users.Object, _policies, _templates, _schedules, recorder);

            _journal = new JournalAdministrationService(_journalService, _valuesCache, _folders.Object,
                _authorization.Object, _users.Object, _engine, _changeSetJournal);
        }


        [Fact]
        public async Task PolicyWrite_LeavesAKindStampedRecordLinkedToTheRequestGroup()
        {
            var sensor = await CreateSensorAsync("jrnPolicy");
            var changeSetId = Guid.NewGuid();

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            var record = RecordOf(changeSetId);

            Assert.Equal(JournalKinds.SensorDataPolicies, record.Kind);
            Assert.Equal(changeSetId.ToString(), record.ChangeSetId);
            Assert.Equal(_fixture.ProductAId.ToString(), record.ProductId);
            Assert.True(record.Rollbackable);
        }

        [Fact]
        public async Task WebEditorFullListWrite_JournalsBothLists_UnderOneRequestGroup()
        {
            // The single write path (#1588): the web editor's full-list save
            // rides the policy administration service, so both lists journal
            // under the post's request group — exactly like a REST write.
            var sensor = await CreateSensorAsync("jrnWebFullList");
            var changeSetId = Guid.NewGuid();
            var initiator = InitiatorInfo.AsUser("web-user");

            // The web form re-asserts the full list (full-list semantics),
            // like the editor's save. The data arm CHANGES the list (the
            // seeded policy is dropped); the TTL arm re-asserts an empty
            // list — unchanged, so it journals nothing (#1588 review round 1).
            var seeded = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser);
            Assert.True(seeded.Success, seeded.Failure?.Message);

            var write = await _policies.WriteSensorListsAsync(sensor.Id,
                dataPolicies: [],
                ttlPolicies: [], TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(write.Success, write.Failure?.Message);

            var group = new ChangeSetJournal(_databaseCoreManager.DatabaseCore).Get(changeSetId);

            Assert.NotNull(group);
            var entry = Assert.Single(group.Entries); // the changed data list only
            Assert.Equal(ChangeSetEntryKind.SensorDataPolicies, entry.Kind);
            Assert.Empty(sensor.Policies);
        }

        [Fact]
        public async Task WebEditorTtlChange_JournalRecordCarriesTheRequestGroup()
        {
            // #1588 review round 3: the web form's TTL updates carry a bare
            // user initiator; the service must re-stamp the list so the
            // sensorTtlPolicies journal record keeps its group link (the TTL
            // collection journals per-item records with the ITEM initiator).
            var sensor = await CreateSensorAsync("jrnWebTtl");
            var changeSetId = Guid.NewGuid();

            var seeded = await _policies.CreateSensorTtlPolicyAsync(sensor.Id, TtlPolicy(), TokenUser);
            Assert.True(seeded.Success, seeded.Failure?.Message);

            var webInitiator = InitiatorInfo.AsUser("web-user");

            // A REAL TTL change through the web form's path: the bare-user
            // updates drop the seeded policy (full-list semantics).
            var cleared = new System.Collections.Generic.List<PolicyUpdate>();

            var write = await _policies.WriteSensorListsAsync(sensor.Id,
                dataPolicies: null,
                ttlPolicies: cleared, TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(write.Success, write.Failure?.Message);

            var record = RecordOf(changeSetId);

            Assert.Equal(JournalKinds.SensorTtlPolicies, record.Kind);
            Assert.Equal(changeSetId.ToString(), record.ChangeSetId);
            Assert.True(record.Rollbackable);
        }

        [Fact]
        public async Task TemplateWrite_LeavesAJournalRecord()
        {
            // Templates have no cache journal hook — the recorder IS their
            // journal writer (#1588).
            var changeSetId = Guid.NewGuid();

            var created = await new AlertTemplateAdministrationService(_valuesCache, _folders.Object, _chats.Object,
                    _alertScheduleProvider, _authorization.Object, _users.Object,
                    new ChangeSetRecorder(new ChangeSetJournal(_databaseCoreManager.DatabaseCore), _journalService,
                        NullLogger<ChangeSetRecorder>.Instance),
                    NullLogger<AlertTemplateAdministrationService>.Instance)
                .CreateTemplateAsync(TokenUser, TemplateDto(["*/jrnTemplate"]),
                    new ChangeSetWriteOptions { ChangeSetId = changeSetId });

            Assert.True(created.Success, created.Failure?.Message);

            var record = RecordOf(changeSetId);

            Assert.Equal(JournalKinds.AlertTemplate, record.Kind);
            Assert.Equal(changeSetId.ToString(), record.ChangeSetId);
            Assert.True(record.Rollbackable);
        }

        [Fact]
        public async Task TemplateWrite_RecordIsVisibleThroughTheRealSightEvaluator()
        {
            // #1610: the template record's sight must resolve like any other
            // record — through the template's FOLDER, never through a
            // product-stamped folder id (the stamp made boundary resolution
            // fail closed and hid every alertTemplate record, admin
            // included). The real evaluator replaces the always-allow mock
            // the suite's other tests ride on.
            var changeSetId = Guid.NewGuid();

            var created = await _templates.CreateTemplateAsync(TokenUser, TemplateDto(["*/jrnRealSight"]),
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            var journal = RealSightJournal(_owner);
            var listed = journal.ListRecords(TokenUser,
                new JournalFilter { Kind = JournalKinds.AlertTemplate, FromUtc = DateTime.UtcNow.AddMinutes(-5) }).Value;

            var record = Assert.Single(listed, r => r.ChangeSetId == changeSetId.ToString());

            Assert.True(record.Rollbackable);
            Assert.Null(record.ProductId); // folder-scoped entities carry no product (journal feature.md)
            Assert.Contains(_owner.Name, record.Author, StringComparison.Ordinal); // the token's owner is the author

            var details = journal.GetRecord(record.Id, TokenUser);

            Assert.True(details.Success, details.Failure?.Message);
            Assert.NotNull(details.Value.ChangeSet);
            var entry = Assert.Single(details.Value.ChangeSet.Entries);

            Assert.Equal("alertTemplate", entry.Kind);
            Assert.NotNull(entry.AfterTemplate); // the group image carries the template body
        }

        [Fact]
        public async Task TemplateWrite_FanOutPolicyRecordsShareTheRequestGroup()
        {
            // #1610: the fan-out of a template write is a policy write of the
            // same request — its sensorDataPolicies records must carry the
            // request's undo group instead of standing alone, exactly like a
            // direct policy write (#1588's linking rule).
            var sensor = await CreateSensorAsync("jrnFanOut");
            var changeSetId = Guid.NewGuid();

            var created = await _templates.CreateTemplateAsync(TokenUser,
                TemplateDto(["*/jrnFanOut"]) with { Policies = [TemplatePolicy()] },
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);
            Assert.Single(sensor.Policies); // the fan-out applied a template policy

            var fanOut = RawRecords(sensor.Id, JournalKinds.SensorDataPolicies);

            var record = Assert.Single(fanOut, r => r.ChangeSetId == changeSetId.ToString());

            // The fan-out author stays the AlertTemplate initiator (the
            // change-table ownership type), only the group link is added.
            Assert.Contains("AlertTemplate", record.Initiator, StringComparison.Ordinal);
            Assert.True(JournalKinds.IsRollbackable(record.Kind));
        }

        [Fact]
        public async Task TemplateDelete_JournalsTheRecord_AndTheStripSharesItsGroup()
        {
            var sensor = await CreateSensorAsync("jrnDelete");
            var template = TemplateDto(["*/jrnDelete"]) with { Policies = [TemplatePolicy()] };

            var created = await _templates.CreateTemplateAsync(TokenUser, template);
            Assert.True(created.Success, created.Failure?.Message);

            var deleteGroup = Guid.NewGuid();
            var deleted = await _templates.DeleteTemplateAsync(created.Value.Id, TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = deleteGroup });
            Assert.True(deleted.Success, deleted.Failure?.Message);
            Assert.Empty(sensor.Policies); // the strip removed the template policy

            // The delete's own alertTemplate record — visible through the
            // real sight evaluator even though the template row is gone
            // (records keyed by deleted entities resolve to Global, the
            // admin-only scope).
            var journal = RealSightJournal(_owner);
            var listed = journal.ListRecords(TokenUser,
                new JournalFilter { Kind = JournalKinds.AlertTemplate, FromUtc = DateTime.UtcNow.AddMinutes(-5) }).Value;

            var deleteRecord = Assert.Single(listed, r => r.ChangeSetId == deleteGroup.ToString());

            Assert.Equal("AlertTemplate:Delete", deleteRecord.Property);
            Assert.True(deleteRecord.Rollbackable);

            // The strip's per-sensor records share the delete's group.
            var strip = RawRecords(sensor.Id, JournalKinds.SensorDataPolicies);

            Assert.Contains(strip, r => r.ChangeSetId == deleteGroup.ToString());
        }

        [Fact]
        public void RecordStampedWithAFolderIdAsProduct_StaysVisible()
        {
            // #1610 repair: records written before the fix carry their FOLDER
            // id in ProductId (the recorder stamped entry.ParentId blindly).
            // The unresolvable stamp must not hide the stored record — sight
            // falls through to the live-tree resolution.
            _journalService.AddRecord(new JournalRecordModel(Guid.NewGuid(), InitiatorInfo.AsUser("legacy-writer"))
            {
                Enviroment = "Alert template",
                PropertyName = "AlertTemplate:Create",
                Path = "legacy-template",
                Kind = JournalKinds.AlertTemplate,
                ProductId = _fixture.FolderId.ToString(), // the pre-fix mis-stamp
                ChangeSetId = Guid.NewGuid().ToString(),
            });

            var listed = RealSightJournal(_owner).ListRecords(TokenUser,
                new JournalFilter { Kind = JournalKinds.AlertTemplate, FromUtc = DateTime.UtcNow.AddMinutes(-5) }).Value;

            var record = Assert.Single(listed, r => r.Path == "legacy-template");

            Assert.True(record.Rollbackable);
        }

        [Fact]
        public async Task Rollback_ByRecord_UndoesTheGroup_AndLeavesARedoRecord()
        {
            var sensor = await CreateSensorAsync("jrnRollback");
            var changeSetId = Guid.NewGuid();

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);
            Assert.Single(sensor.Policies); // the write applied

            var record = RecordOf(changeSetId);

            var rolledBack = await _journal.RollbackAsync(record.Id, TokenUser);

            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);
            Assert.Equal("completed", rolledBack.Value.Status);

            // The policy is gone and the rollback group left its own
            // journal record (the redo handle is visible in the journal).
            Assert.Empty(sensor.Policies);

            var redoId = Guid.Parse(rolledBack.Value.RollbackChangeSetId);
            var redoRecord = RecordOf(redoId);

            Assert.Equal(JournalKinds.SensorDataPolicies, redoRecord.Kind);
            Assert.True(redoRecord.Rollbackable);
        }

        [Fact]
        public async Task ListRecords_OperationTagNamesCreateUpdateDeleteAndRollback()
        {
            // The QA scenario of #1638: create, edit, rollback, rollback of
            // the rollback (redo), delete — every row must name its
            // operation at a glance, and the rolled-back edit must be
            // visibly marked instead of only losing its Rollback button.
            var createGroup = Guid.NewGuid();
            var updateGroup = Guid.NewGuid();
            var deleteGroup = Guid.NewGuid();

            var created = await _templates.CreateTemplateAsync(TokenUser, TemplateDto(["*/jrnOps"]),
                new ChangeSetWriteOptions { ChangeSetId = createGroup });
            Assert.True(created.Success, created.Failure?.Message);

            var updated = await _templates.UpdateTemplateAsync(created.Value.Id, TokenUser,
                TemplateDto(["*/jrnOps"]),
                new ChangeSetWriteOptions { ChangeSetId = updateGroup });
            Assert.True(updated.Success, updated.Failure?.Message);

            var rolledBack = await _journal.RollbackAsync(RecordOf(updateGroup).Id, TokenUser);
            Assert.True(rolledBack.Success, rolledBack.Failure?.Message);

            var redoGroup = Guid.Parse(rolledBack.Value.RollbackChangeSetId);

            var deleted = await _templates.DeleteTemplateAsync(created.Value.Id, TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = deleteGroup });
            Assert.True(deleted.Success, deleted.Failure?.Message);

            var listed = _journal.ListRecords(TokenUser,
                new JournalFilter { Kind = JournalKinds.AlertTemplate, FromUtc = DateTime.UtcNow.AddMinutes(-5), Limit = 200 }).Value;

            Assert.Equal("create", listed.Single(r => r.ChangeSetId == createGroup.ToString()).Operation);
            Assert.Equal("delete", listed.Single(r => r.ChangeSetId == deleteGroup.ToString()).Operation);
            Assert.Equal("rollback", listed.Single(r => r.ChangeSetId == redoGroup.ToString()).Operation);

            var edit = listed.Single(r => r.ChangeSetId == updateGroup.ToString());

            Assert.Equal("update", edit.Operation);
            Assert.True(edit.RolledBack);
            Assert.False(edit.Rollbackable);

            // The details view agrees with the table row (#1638).
            var details = _journal.GetRecord(edit.Id, TokenUser);

            Assert.True(details.Success, details.Failure?.Message);
            Assert.True(details.Value.Record.RolledBack);
            Assert.Equal("update", details.Value.Record.Operation);
        }

        [Fact]
        public async Task ListRecords_PolicyRow_NamesTheOperationFromItsGroupEntry()
        {
            // Policy rows carry no "{Kind}:{Operation}" property — the
            // operation comes from their undo-group entry.
            var sensor = await CreateSensorAsync("jrnPolicyOp");
            var changeSetId = Guid.NewGuid();

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            var record = RecordOf(changeSetId);

            Assert.Equal("create", record.Operation);
            Assert.False(record.RolledBack);
        }

        [Fact]
        public void ListRecords_LegacyRowWithoutOperation_RendersNullOperation()
        {
            // Rows with no derivable operation (legacy policy items, rows
            // written before the tags) must keep rendering — no badge, no
            // exception.
            _journalService.AddRecord(new JournalRecordModel(Guid.NewGuid(), InitiatorInfo.AsUser("legacy-op-writer"))
            {
                Enviroment = "Alert collection",
                PropertyName = "Alert",
                OldValue = "off",
                NewValue = "on",
            });

            var listed = _journal.ListRecords(TokenUser,
                new JournalFilter { FromUtc = DateTime.UtcNow.AddMinutes(-5), Limit = 200 }).Value;

            var record = listed.Single(r => r.Author.Contains("legacy-op-writer", StringComparison.Ordinal));

            Assert.Null(record.Operation);
            Assert.False(record.RolledBack);
        }

        [Fact]
        public async Task GetRecord_TemplateFanout_IsABoundedPageByDefault()
        {
            // #1688: the per-entry fan-out images grow linearly with the
            // number of sensors a template touched (about 1.2 KB each), so an
            // unbounded render made the record unreadable through MCP past a
            // few dozen sensors. The default page is bounded, fanoutTotal
            // carries the honest count, and the caller pages with
            // fanoutOffset/fanoutLimit.
            const int sensors = 45;

            for (var i = 0; i < sensors; i++)
                await CreateSensorAsync($"jrnFanout/s{i}");

            var changeSetId = Guid.NewGuid();
            var created = await _templates.CreateTemplateAsync(TokenUser, TemplateDto(["*/jrnFanout/*"]),
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            var record = RecordOf(changeSetId);

            var defaultDetails = _journal.GetRecord(record.Id, TokenUser).Value;
            var defaultEntry = Assert.Single(defaultDetails.ChangeSet.Entries);

            Assert.Equal(sensors, defaultEntry.FanoutTotal);
            Assert.Equal(ChangeSetAdministrationService.DefaultFanoutLimit, defaultEntry.Fanout.Count);

            var next = _journal.GetRecord(record.Id, TokenUser,
                fanoutOffset: ChangeSetAdministrationService.DefaultFanoutLimit).Value;

            // The offset pages; the limit stays the bounded default.
            Assert.Equal(ChangeSetAdministrationService.DefaultFanoutLimit, Assert.Single(next.ChangeSet.Entries).Fanout.Count);

            var lastFive = _journal.GetRecord(record.Id, TokenUser, fanoutOffset: sensors - 5, fanoutLimit: 5).Value;

            Assert.Equal(5, Assert.Single(lastFive.ChangeSet.Entries).Fanout.Count);

            // An oversized limit never buys an unbounded render: it falls to
            // the ceiling, and the page is still bounded by what exists.
            var capped = _journal.GetRecord(record.Id, TokenUser, fanoutLimit: int.MaxValue).Value;

            Assert.Equal(sensors, Assert.Single(capped.ChangeSet.Entries).Fanout.Count);
        }


        [Fact]
        public async Task Rollback_ByRecord_NonAdminTokenOwner_IsForbidden()
        {
            // #1588 breaking change: Manager tokens can no longer roll back —
            // the token's owner must be an admin (the per-resource write
            // checks stay as the second line of defense inside the engine).
            var sensor = await CreateSensorAsync("jrnNonAdmin");
            var changeSetId = Guid.NewGuid();

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            _owner.IsAdmin = false;

            var refused = await _journal.RollbackAsync(RecordOf(changeSetId).Id, TokenUser);

            Assert.False(refused.Success);
            Assert.Equal(PolicyWriteOutcome.Forbidden, refused.Failure.Outcome);
            Assert.Equal(JournalAdministrationService.JournalRollbackAccessDeniedMessage, refused.Failure.Message);
        }

        [Fact]
        public async Task Rollback_ByNonRollbackableRecord_IsConflict()
        {
            // Tree CRUD and node settings are journal-visible only — no undo
            // group, no rollback.
            var productId = Guid.NewGuid();

            _journalService.AddRecord(new JournalRecordModel(productId, InitiatorInfo.AsUser("tree-admin"))
            {
                Enviroment = "Create product",
                PropertyName = "product",
                Kind = JournalKinds.TreeCrud,
                ProductId = productId.ToString(),
            });

            // The record under test, addressed uniquely: the shared fixture
            // DB accumulates records from the whole class, and under the
            // #1601 semantics global/folder-scoped records also match a
            // product-only filter — so the entity id is the single-record
            // lookup here.
            var listed = _journal.ListRecords(TokenUser,
                new JournalFilter { ProductId = productId, EntityId = productId }).Value;
            var record = Assert.Single(listed);

            Assert.False(record.Rollbackable);

            var refused = await _journal.RollbackAsync(record.Id, TokenUser);

            Assert.False(refused.Success);
            Assert.Equal(PolicyWriteOutcome.Conflict, refused.Failure.Outcome);
        }

        [Fact]
        public async Task ListRecords_FiltersByKindAuthorAndProduct()
        {
            var sensorA = await CreateSensorAsync("jrnFilterA");
            var sensorB = await CreateSensorAsync("jrnFilterB");
            var groupA = Guid.NewGuid();
            var groupB = Guid.NewGuid();

            var first = await _policies.CreateSensorPolicyAsync(sensorA.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = groupA });
            var second = await _policies.CreateSensorPolicyAsync(sensorB.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = groupB });
            Assert.True(first.Success, first.Failure?.Message);
            Assert.True(second.Success, second.Failure?.Message);

            var byKind = _journal.ListRecords(TokenUser,
                new JournalFilter { Kind = JournalKinds.SensorDataPolicies, FromUtc = DateTime.UtcNow.AddMinutes(-5) }).Value;

            Assert.Contains(byKind, r => r.ChangeSetId == groupA.ToString());
            Assert.Contains(byKind, r => r.ChangeSetId == groupB.ToString());
            Assert.All(byKind, r => Assert.Equal(JournalKinds.SensorDataPolicies, r.Kind));

            // A product-scoped record of another product: must stay hidden.
            _journalService.AddRecord(new JournalRecordModel(_fixture.ProductBId, InitiatorInfo.AsUser("b-admin"))
            {
                Enviroment = "Create product",
                PropertyName = "product",
                Kind = JournalKinds.TreeCrud,
                ProductId = _fixture.ProductBId.ToString(),
            });

            var byProduct = _journal.ListRecords(TokenUser,
                new JournalFilter { ProductId = _fixture.ProductAId, FromUtc = DateTime.UtcNow.AddMinutes(-5) }).Value;

            // The own product's records stay; another product's record
            // stays hidden. (The global/folder-scoped visibility half of
            // the #1601 semantics is pinned by
            // ListRecords_WithProductFilter_KeepsGlobalAndFolderScopedRecordsVisible.)
            Assert.Contains(byProduct, r => r.ChangeSetId == groupA.ToString());
            Assert.Contains(byProduct, r => r.ChangeSetId == groupB.ToString());
            Assert.DoesNotContain(byProduct, r => r.ProductId == _fixture.ProductBId.ToString());
        }

        [Fact]
        public async Task ListRecords_WithProductFilter_KeepsGlobalAndFolderScopedRecordsVisible()
        {
            // #1601: alert schedules are global and alert templates are
            // folder-scoped — neither carries a root product, and a product
            // filter must keep them visible instead of rendering the page
            // empty; records of ANOTHER root product stay hidden.
            var scheduleGroup = Guid.NewGuid();
            var templateGroup = Guid.NewGuid();

            var schedule = await _schedules.CreateScheduleAsync(TokenUser, ScheduleDto(),
                new ChangeSetWriteOptions { ChangeSetId = scheduleGroup });
            Assert.True(schedule.Success, schedule.Failure?.Message);

            var template = await new AlertTemplateAdministrationService(_valuesCache, _folders.Object, _chats.Object,
                    _alertScheduleProvider, _authorization.Object, _users.Object,
                    new ChangeSetRecorder(new ChangeSetJournal(_databaseCoreManager.DatabaseCore), _journalService,
                        NullLogger<ChangeSetRecorder>.Instance),
                    NullLogger<AlertTemplateAdministrationService>.Instance)
                .CreateTemplateAsync(TokenUser, TemplateDto(["*/jrnGlobalKeep"]),
                    new ChangeSetWriteOptions { ChangeSetId = templateGroup });
            Assert.True(template.Success, template.Failure?.Message);

            // A product-scoped record of another LIVE product: must stay hidden.
            _journalService.AddRecord(new JournalRecordModel(_fixture.ProductBId, InitiatorInfo.AsUser("other-admin"))
            {
                Enviroment = "Create product",
                PropertyName = "product",
                Kind = JournalKinds.TreeCrud,
                ProductId = _fixture.ProductBId.ToString(),
            });

            // #1624 review: a record stamped with a product that is NOT
            // live (deleted) is still PRODUCT-scoped — its history must
            // not leak into another product's filter.
            var deadProductId = Guid.NewGuid();
            _journalService.AddRecord(new JournalRecordModel(deadProductId, InitiatorInfo.AsUser("gone-admin"))
            {
                Enviroment = "Delete product",
                PropertyName = "product",
                Kind = JournalKinds.TreeCrud,
                ProductId = deadProductId.ToString(),
            });

            var byProduct = _journal.ListRecords(TokenUser,
                new JournalFilter { ProductId = _fixture.ProductAId, FromUtc = DateTime.UtcNow.AddMinutes(-5), Limit = 200 }).Value;

            Assert.Contains(byProduct, r => r.ChangeSetId == scheduleGroup.ToString()); // global: visible
            Assert.Contains(byProduct, r => r.ChangeSetId == templateGroup.ToString()); // folder-scoped: visible
            Assert.DoesNotContain(byProduct, r => r.ProductId == _fixture.ProductBId.ToString()); // other product: hidden
            Assert.DoesNotContain(byProduct, r => r.ProductId == deadProductId.ToString()); // dead product: hidden too
        }

        [Fact]
        public async Task GetRecord_ReturnsTheUndoGroupDetails()
        {
            var sensor = await CreateSensorAsync("jrnDetails");
            var changeSetId = Guid.NewGuid();

            var created = await _policies.CreateSensorPolicyAsync(sensor.Id, DataPolicy(), TokenUser,
                new ChangeSetWriteOptions { ChangeSetId = changeSetId });
            Assert.True(created.Success, created.Failure?.Message);

            var record = RecordOf(changeSetId);
            var details = _journal.GetRecord(record.Id, TokenUser);

            Assert.True(details.Success, details.Failure?.Message);
            Assert.NotNull(details.Value.ChangeSet);
            var entry = Assert.Single(details.Value.ChangeSet.Entries);

            Assert.Equal("sensorDataPolicies", entry.Kind);
        }

        [Fact]
        public void GetRecord_ForAnIdInAnUnretainedWeek_IsNotFound_AndMintsNoShard()
        {
            // #1588 review round 2: the record id's ticks are caller-crafted;
            // the exact-key read must not CREATE the week's shard (the write
            // path's GetDatabaseByTime does) — an unbounded id space would
            // otherwise mint empty LevelDB shards on the read path.
            var ancient = new JournalKey(Guid.NewGuid(), new DateTime(2000, 1, 1).Ticks,
                HSMDatabase.AccessManager.DatabaseEntities.RecordType.Changes);
            var recordId = $"{ancient.Id:N}:{ancient.Time}:{(int)ancient.Type}";

            var journalFolder = System.IO.Path.Combine(_databaseCoreManager.DatabaseFolder, "Journals");
            var shardsBefore = System.IO.Directory.Exists(journalFolder)
                ? System.IO.Directory.GetDirectories(journalFolder).Length
                : 0;

            var resolved = _journal.GetRecord(recordId, TokenUser);

            Assert.False(resolved.Success);
            Assert.Equal(PolicyWriteOutcome.NotFound, resolved.Failure.Outcome);

            var shardsAfter = System.IO.Directory.Exists(journalFolder)
                ? System.IO.Directory.GetDirectories(journalFolder).Length
                : 0;

            Assert.Equal(shardsBefore, shardsAfter);
        }

        [Fact]
        public void RequestGroupId_IsMintedOncePerHttpContext()
        {
            // One HTTP request = one rollback group (#1588): the id is
            // minted on first use and cached in HttpContext.Items.
            var context = new DefaultHttpContext();

            var first = ChangeSetHeaders.RequestGroupId(context);
            var second = ChangeSetHeaders.RequestGroupId(context);

            Assert.NotEqual(Guid.Empty, first);
            Assert.Equal(first, second);

            var other = ChangeSetHeaders.RequestGroupId(new DefaultHttpContext());

            Assert.NotEqual(first, other);
        }


        private JournalRecordDto RecordOf(Guid changeSetId)
        {
            var listed = _journal.ListRecords(TokenUser,
                new JournalFilter { FromUtc = DateTime.UtcNow.AddMinutes(-5), Limit = 200 }).Value;

            return listed.Single(r => r.ChangeSetId == changeSetId.ToString());
        }

        // A journal service wired to the REAL sight evaluator (#1610): the
        // suite's always-allow authorization mock hides nothing, and the
        // template-record regression this fix pins — a folder id stamped
        // into ProductId failing closed under Product() boundary resolution
        // — only reproduces through the production decision path. The owner
        // resolves through the users manager, the token is live, the
        // fixture's folder through the folder manager, products and sensors
        // through the live cache.
        private JournalAdministrationService RealSightJournal(User owner)
        {
            var users = new Mock<IUserManager>();
            users.Setup(u => u[It.IsAny<Guid>()]).Returns(owner);

            var tokens = new Mock<IApiTokenManager>();
            tokens.Setup(t => t.IsTokenLive(It.IsAny<string>())).Returns(true);
            tokens.Setup(t => t.GetToken(It.IsAny<string>())).Returns(new ApiTokenInfo { Name = "token" });

            var folder = new FolderModel(EntitiesFactory.BuildFolderEntity() with
            {
                Id = _fixture.FolderId.ToString(),
            });
            var folders = new Mock<IFolderManager>();
            folders.Setup(f => f.TryGetValue(_fixture.FolderId, out folder)).Returns(true);

            var authorization = new ApiTokenAuthorizationService(users.Object, tokens.Object, folders.Object,
                _valuesCache, new Mock<IApiTokenSecurityEventSink>().Object);

            return new JournalAdministrationService(_journalService, _valuesCache, folders.Object,
                authorization, users.Object, _engine, _changeSetJournal);
        }

        // The raw storage rows of one entity under a kind tag (#1610): the
        // journal models straight from the core service, below the DTO
        // rendering — the fan-out records' initiator and group link live on
        // the raw row.
        private List<JournalRecordModel> RawRecords(Guid entityId, string kind) =>
            _journalService.GetRecords(DateTime.UtcNow.AddMinutes(-5), DateTime.UtcNow)
                .Where(r => r.Key.Id == entityId && r.Kind == kind)
                .ToList();

        private async Task<BaseSensorModel> CreateSensorAsync(string path)
        {
            var value = SensorValuesFactory.BuildSensorValue(SensorType.Integer, path, DateTime.UtcNow);
            var result = await _valuesCache.AddSensorValueAsync(_fixture.AccessKeyAId, _fixture.ProductAId, value);
            Assert.True(result.IsOk, result.Error);

            Assert.True(_valuesCache.TryGetSensorByPath(_fixture.ProductAId, path, out var sensor));

            return sensor;
        }

        private static ClaimsPrincipal BuildPrincipal() =>
            new(new ClaimsIdentity(
            [
                new Claim(HsmApiTokenClaims.OwnerUserId, Guid.NewGuid().ToString()),
                new Claim(HsmApiTokenClaims.TokenId, new string('A', ApiTokenMaterial.TokenIdLength)),
            ], HsmApiTokenDefaults.AuthenticationScheme));

        private static PolicyDto DataPolicy() => new()
        {
            Icon = "policy",
            Conditions =
            [
                new AlertConditionDto
                {
                    Property = "Value",
                    Operation = "GreaterThan",
                    Target = System.Text.Json.JsonSerializer.SerializeToElement(20),
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

        private AlertTemplateDto TemplateDto(string[] paths) => new()
        {
            Name = $"template-{Guid.NewGuid():N}",
            SensorType = (byte)SensorType.Integer,
            FolderId = _fixture.FolderId,
            Paths = [.. paths],
            Policies = [],
        };

        // The template flavor of a data policy (AlertPolicyDto, not the
        // direct-write PolicyDto): one Value > 20 condition, applied to the
        // fan-out targets of the template's path patterns.
        private static AlertPolicyDto TemplatePolicy() => new()
        {
            Conditions =
            [
                new PolicyConditionDto
                {
                    Target = new PolicyTargetDto { Type = (byte)TargetType.Const, Value = "20" },
                    Combination = (byte)PolicyCombination.And,
                    Operation = (byte)PolicyOperation.GreaterThan,
                    Property = (byte)PolicyProperty.Value,
                },
            ],
            SensorStatus = (byte)SensorStatus.Ok,
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
