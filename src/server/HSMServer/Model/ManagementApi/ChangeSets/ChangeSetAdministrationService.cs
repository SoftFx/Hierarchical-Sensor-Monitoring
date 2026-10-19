using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Model;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.AlertSchedules;
using HSMServer.Model.ManagementApi.AlertTemplates;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;

namespace HSMServer.Model.ManagementApi.ChangeSets
{
    /// <summary>
    /// The read and rollback surface of the undo journal (#1570):
    /// transport-agnostic list/get with caller-sight filtering, and the
    /// rollback engine — compensating writes replayed in reverse journal
    /// order through the same administration services, write gates and
    /// authorization the original writes used.
    /// </summary>
    // Conflict rule (#1570): every affected entity's CURRENT content is
    // compared to the recorded after-image before anything is restored —
    // content comparison, never timestamps (the web editor re-asserts full
    // lists on every save). A mismatch blocks the rollback with a per-entity
    // diff unless the caller forces it. A rollback is itself journaled as a
    // new change set, which makes redo a rollback of the rollback.
    // The controller-facing contract of the undo-journal surface: mocked in
    // the REST contract tests, implemented by ChangeSetAdministrationService.
    public interface IChangeSetAdministrationService
    {
        PolicyWriteResult<List<ChangeSetSummaryDto>> ListChangeSets(ClaimsPrincipal user, ChangeSetFilter filter);

        PolicyWriteResult<ChangeSetDetailsDto> GetChangeSet(Guid id, ClaimsPrincipal user);

        Task<PolicyWriteResult<RollbackResultDto>> RollbackAsync(Guid id, ClaimsPrincipal user,
            bool force = false, bool dryRun = false);
    }


    public sealed class ChangeSetAdministrationService : IChangeSetAdministrationService
    {
        private const int MaxLimit = 200;

        private static readonly JsonSerializerOptions ImageJson = new()
        {
            WriteIndented = true,
        };

        private readonly IChangeSetJournal _journal;
        private readonly ITreeValuesCache _cache;
        private readonly IAlertScheduleProvider _schedules;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;
        private readonly PolicyAdministrationService _policies;
        private readonly AlertTemplateAdministrationService _templates;
        private readonly AlertScheduleAdministrationService _scheduleWrites;
        private readonly ChangeSetRecorder _recorder;

        public ChangeSetAdministrationService(IChangeSetJournal journal, ITreeValuesCache cache,
            IAlertScheduleProvider schedules, IApiTokenAuthorizationService authorization, IUserManager users,
            PolicyAdministrationService policies, AlertTemplateAdministrationService templates,
            AlertScheduleAdministrationService scheduleWrites, ChangeSetRecorder recorder)
        {
            _journal = journal;
            _cache = cache;
            _schedules = schedules;
            _authorization = authorization;
            _users = users;
            _policies = policies;
            _templates = templates;
            _scheduleWrites = scheduleWrites;
            _recorder = recorder;
        }


        // === List / Get ===

        public PolicyWriteResult<List<ChangeSetSummaryDto>> ListChangeSets(ClaimsPrincipal user,
            ChangeSetFilter filter)
        {
            filter ??= new ChangeSetFilter();

            var visible = _journal.GetAll()
                .Where(set => set.Entries.Any(e => EntryIsVisible(e, user)))
                .Where(set => Matches(set, filter))
                .Select(set => RenderSummary(set, user))
                .ToList();

            var limit = Math.Clamp(filter.Limit <= 0 ? 50 : filter.Limit, 1, MaxLimit);
            var offset = Math.Max(filter.Offset, 0);

            return PolicyWriteResult<List<ChangeSetSummaryDto>>.Ok(visible.Skip(offset).Take(limit).ToList());
        }

        public PolicyWriteResult<ChangeSetDetailsDto> GetChangeSet(Guid id, ClaimsPrincipal user)
        {
            var set = _journal.Get(id);

            // Anti-enumeration: an unknown id and an invisible change set
            // answer the same 404.
            if (set is null || set.Entries.All(e => !EntryIsVisible(e, user)))
                return PolicyWriteResult<ChangeSetDetailsDto>.Fail(PolicyWriteOutcome.NotFound);

            return PolicyWriteResult<ChangeSetDetailsDto>.Ok(RenderDetails(set, user));
        }


        // === Rollback ===

        public async Task<PolicyWriteResult<RollbackResultDto>> RollbackAsync(Guid id, ClaimsPrincipal user,
            bool force = false, bool dryRun = false)
        {
            var set = _journal.Get(id);

            if (set is null || set.Entries.All(e => !EntryIsVisible(e, user)))
                return PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.NotFound);

            if (set.Status == ChangeSetStatus.RolledBack)
                return PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.Conflict,
                    message: "The change set was already rolled back. Roll back its rollback change set to redo it.");

            var pending = set.Entries.Where(e => e.RestoredBy is null).OrderBy(e => e.Sequence).ToList();

            // Pre-flight: authorization on every pending entry (the same
            // boundaries the original writes passed), then the content
            // comparison against each after-image.
            var denied = pending.FirstOrDefault(e => !EntryIsWritable(e, user));

            if (denied is not null)
                return PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.Forbidden,
                    message: PolicyWriteResult.WriteDeniedMessage);

            var reports = new List<RollbackEntryReportDto>();
            var conflicts = pending
                .Select(e => (Entry: e, Conflicts: CompareWithAfterImage(e)))
                .ToList();

            foreach (var (entry, entryConflicts) in conflicts.OrderByDescending(pair => pair.Entry.Sequence))
                reports.Add(new RollbackEntryReportDto
                {
                    Sequence = entry.Sequence,
                    Kind = KindOf(entry),
                    Operation = OperationOf(entry),
                    Path = entry.Path,
                    Status = dryRun ? "wouldRestore" : "restored",
                    Conflicts = entryConflicts,
                });

            var allConflicts = conflicts.SelectMany(pair => pair.Conflicts).ToList();

            if (dryRun)
                return PolicyWriteResult<RollbackResultDto>.Ok(new RollbackResultDto
                {
                    ChangeSetId = id.ToString(),
                    DryRun = true,
                    Forced = force,
                    Status = allConflicts.Count > 0 ? "conflicts" : "completed",
                    Message = allConflicts.Count > 0
                        ? "Conflicts detected — the rollback needs force to proceed."
                        : "No conflicts — the rollback would restore every pending entry.",
                    Entries = reports,
                });

            if (allConflicts.Count > 0 && !force)
                return PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.Conflict,
                    message:
                    "The change set cannot be rolled back: entities changed after it was recorded. " +
                    "Force the rollback to override, or restore the entities manually.",
                    details: ConflictDetails(conflicts));

            // Execute in REVERSE journal order (the latest write is undone
            // first), skipping already-restored entries of a partial retry.
            var rollbackChangeSetId = Guid.NewGuid();
            var author = AuthorOf(user);
            var failures = 0;
            var restoredAny = false;

            foreach (var entry in pending.OrderByDescending(e => e.Sequence))
            {
                var restored = await RestoreEntryAsync(entry, user, rollbackChangeSetId, author);

                if (restored is not null)
                {
                    failures++;

                    var report = reports.First(r => r.Sequence == entry.Sequence);

                    reports.Remove(report);
                    reports.Add(report with { Status = "failed", Message = restored });
                }
                else
                {
                    restoredAny = true;
                    _journal.MarkEntryRestored(id, entry.Sequence, rollbackChangeSetId);
                }
            }

            var completed = failures == 0 && restoredAny;

            // Terminal only when EVERY entry (including ones restored by an
            // earlier partial retry) is restored.
            if (_journal.Get(id)?.Entries.All(e => e.RestoredBy is not null) == true)
                _journal.MarkRolledBack(id, rollbackChangeSetId);

            return PolicyWriteResult<RollbackResultDto>.Ok(new RollbackResultDto
            {
                ChangeSetId = id.ToString(),
                DryRun = false,
                Forced = force,
                Status = completed ? "completed" : "partial",
                Message = failures == 0
                    ? $"Rolled back; redo via change set {rollbackChangeSetId}."
                    : $"{failures} entr{(failures == 1 ? "y" : "ies")} could not be restored — see the per-entry reports and retry.",
                RollbackChangeSetId = rollbackChangeSetId.ToString(),
                Entries = reports,
            });
        }


        // === Restore execution ===

        // One entry's compensating writes; returns null on success or the
        // failure message. The rollback's own journal entry (before = state
        // at rollback time, after = restored state) is recorded per entry so
        // the redo path sees the same full-list images.
        private async Task<string> RestoreEntryAsync(ChangeSetEntry entry, ClaimsPrincipal user,
            Guid rollbackChangeSetId, InitiatorInfo author)
        {
            switch (entry.Kind)
            {
                case ChangeSetEntryKind.SensorDataPolicies:
                    return await RestoreSensorListAsync(entry, data: entry.Before, ttl: null,
                        user, rollbackChangeSetId, author);

                case ChangeSetEntryKind.SensorTtlPolicies:
                    return await RestoreSensorListAsync(entry, data: null, ttl: entry.Before,
                        user, rollbackChangeSetId, author);

                case ChangeSetEntryKind.ProductTtlPolicies:
                {
                    var before = CaptureProductTtl(entry.EntityId);

                    var result = await _policies.RestoreProductTtlAsync(entry.EntityId, entry.Before, user);

                    if (!result.Success)
                        return result.Failure.Message;

                    RecordRollbackEntry(rollbackChangeSetId, author, entry, before,
                        CaptureProductTtl(entry.EntityId));

                    return null;
                }

                case ChangeSetEntryKind.AlertTemplate:
                    return await RestoreTemplateAsync(entry, user, rollbackChangeSetId, author);

                case ChangeSetEntryKind.AlertSchedule:
                    return await RestoreScheduleAsync(entry, user, rollbackChangeSetId, author);

                default:
                    return $"Unknown entry kind {entry.Kind}.";
            }
        }

        private async Task<string> RestoreSensorListAsync(ChangeSetEntry entry, List<PolicyEntity> data,
            List<PolicyEntity> ttl, ClaimsPrincipal user, Guid rollbackChangeSetId, InitiatorInfo author)
        {
            // Capture the list THIS entry owns (kind decides data vs TTL) —
            // the rollback's own before-image.
            var before = data is not null ? CaptureSensorData(entry.EntityId) : CaptureSensorTtl(entry.EntityId);

            var result = await _policies.RestoreSensorListsAsync(entry.EntityId, data, ttl, user);

            if (!result.Success)
                return result.Failure.Message;

            var after = data is not null ? CaptureSensorData(entry.EntityId) : CaptureSensorTtl(entry.EntityId);

            RecordRollbackEntry(rollbackChangeSetId, author, entry, before, after);

            return null;
        }

        private async Task<string> RestoreTemplateAsync(ChangeSetEntry entry, ClaimsPrincipal user,
            Guid rollbackChangeSetId, InitiatorInfo author)
        {
            var beforeTemplate = _cache.GetAlertTemplate(entry.EntityId)?.ToEntity();

            // The template row first: a CREATE is compensated by the delete
            // (which strips its policies); an UPDATE or DELETE is compensated
            // by restoring the recorded row and re-running its fan-out.
            var result = entry.Operation == ChangeSetOperation.Create
                ? await _templates.RemoveTemplateCoreAsync(entry.EntityId, user)
                : await _templates.RestoreTemplateEntityAsync(entry.BeforeTemplate, user);

            if (!result.Success)
                return result.Failure.Message;

            // Then the per-sensor exact restores: the fan-out images force
            // every touched sensor to its recorded before-list regardless of
            // what the template machinery just did.
            foreach (var image in entry.Fanout ?? [])
            {
                if (image.NodeKind == ChangeSetNodeKind.Product)
                {
                    var productResult = await _policies.RestoreProductTtlAsync(image.NodeId,
                        image.BeforeTtlPolicies, user);

                    if (!productResult.Success)
                        return $"Sensor list restore failed on {image.Path}: {productResult.Failure.Message}";
                }
                else
                {
                    var sensorResult = await _policies.RestoreSensorListsAsync(image.NodeId,
                        image.BeforePolicies, image.BeforeTtlPolicies, user);

                    if (!sensorResult.Success)
                        return $"Sensor list restore failed on {image.Path}: {sensorResult.Failure.Message}";
                }
            }

            RecordRollbackEntry(rollbackChangeSetId, author, entry,
                beforeTemplate: beforeTemplate, afterTemplate: _cache.GetAlertTemplate(entry.EntityId)?.ToEntity());

            return null;
        }

        private async Task<string> RestoreScheduleAsync(ChangeSetEntry entry, ClaimsPrincipal user,
            Guid rollbackChangeSetId, InitiatorInfo author)
        {
            var beforeSchedule = _schedules.GetSchedule(entry.EntityId)?.ToEntity();

            var result = entry.Operation == ChangeSetOperation.Create
                ? await _scheduleWrites.DeleteScheduleCoreAsync(entry.EntityId, user)
                : _scheduleWrites.RestoreScheduleEntityAsync(entry.BeforeSchedule, user);

            if (!result.Success)
                return result.Failure.Message;

            // The detach fan-out: restore the recorded per-node lists and the
            // template rows that carried the schedule reference.
            foreach (var image in entry.Fanout ?? [])
            {
                var fanoutResult = image.NodeKind == ChangeSetNodeKind.Product
                    ? await _policies.RestoreProductTtlAsync(image.NodeId, image.BeforeTtlPolicies, user)
                    : await _policies.RestoreSensorListsAsync(image.NodeId, image.BeforePolicies,
                        image.BeforeTtlPolicies, user);

                if (!fanoutResult.Success)
                    return $"Detach restore failed on {image.Path}: {fanoutResult.Failure.Message}";
            }

            foreach (var templateEntity in entry.BeforeTemplates ?? [])
            {
                var templateResult = await _templates.RestoreTemplateEntityAsync(templateEntity, user);

                if (!templateResult.Success)
                    return $"Template restore failed: {templateResult.Failure.Message}";
            }

            RecordRollbackEntry(rollbackChangeSetId, author, entry,
                beforeSchedule: beforeSchedule, afterSchedule: _schedules.GetSchedule(entry.EntityId)?.ToEntity());

            return null;
        }


        // === Conflict detection (content comparison, #1570) ===

        private List<string> CompareWithAfterImage(ChangeSetEntry entry)
        {
            var conflicts = new List<string>();

            switch (entry.Kind)
            {
                case ChangeSetEntryKind.SensorDataPolicies:
                case ChangeSetEntryKind.SensorTtlPolicies:
                    CompareSensorLists(entry, conflicts);
                    break;

                case ChangeSetEntryKind.ProductTtlPolicies:
                    CompareProductTtl(entry, conflicts);
                    break;

                case ChangeSetEntryKind.AlertTemplate:
                    CompareTemplate(entry, conflicts);

                    foreach (var image in entry.Fanout ?? [])
                        CompareNodeImage(image, conflicts);

                    break;

                case ChangeSetEntryKind.AlertSchedule:
                    CompareSchedule(entry, conflicts);

                    foreach (var image in entry.Fanout ?? [])
                        CompareNodeImage(image, conflicts);

                    foreach (var (before, index) in (entry.BeforeTemplates ?? []).Select((t, i) => (t, i)))
                    {
                        var after = (entry.AfterTemplates ?? []).ElementAtOrDefault(index);
                        var current = _cache.GetAlertTemplate(new Guid(before.Id))?.ToEntity();

                        if (!TemplateRowsEqual(after, current))
                            conflicts.Add($"template {before.Name}: changed after the journaled write");
                    }

                    break;
            }

            return conflicts;
        }

        private void CompareSensorLists(ChangeSetEntry entry, List<string> conflicts)
        {
            var sensor = _cache.GetSensor(entry.EntityId);

            if (sensor is null)
            {
                conflicts.Add($"{entry.Path}: sensor deleted after the journaled write");
                return;
            }

            var live = entry.Kind == ChangeSetEntryKind.SensorDataPolicies
                ? ChangeSetRecorder.SnapshotDataPolicies(sensor)
                : ChangeSetRecorder.SnapshotSensorTtlPolicies(sensor);

            AddListConflicts(entry.Path, entry.After, live, conflicts);
        }

        private void CompareProductTtl(ChangeSetEntry entry, List<string> conflicts)
        {
            if (!_cache.TryGetProduct(entry.EntityId, out var product) || product is null)
            {
                conflicts.Add($"{entry.Path}: product deleted after the journaled write");
                return;
            }

            AddListConflicts(entry.Path, entry.After,
                ChangeSetRecorder.SnapshotProductTtlPolicies(product), conflicts);
        }

        private void CompareTemplate(ChangeSetEntry entry, List<string> conflicts)
        {
            var current = _cache.GetAlertTemplate(entry.EntityId)?.ToEntity();

            if (!TemplateRowsEqual(entry.AfterTemplate, current))
                conflicts.Add($"{entry.Path}: template changed after the journaled write");
        }

        private void CompareSchedule(ChangeSetEntry entry, List<string> conflicts)
        {
            var current = _schedules.GetSchedule(entry.EntityId)?.ToEntity();
            var after = entry.AfterSchedule;

            if ((after is null) != (current is null) ||
                (after is not null && current is not null &&
                 (after.Name != current.Name || after.Timezone != current.Timezone || after.Schedule != current.Schedule)))
                conflicts.Add($"{entry.Path}: schedule changed after the journaled write");
        }

        private void CompareNodeImage(NodePolicyImage image, List<string> conflicts)
        {
            if (image.NodeKind == ChangeSetNodeKind.Product)
            {
                if (!_cache.TryGetProduct(image.NodeId, out var product) || product is null)
                {
                    conflicts.Add($"{image.Path}: product deleted after the journaled write");
                    return;
                }

                AddListConflicts(image.Path, image.AfterTtlPolicies,
                    ChangeSetRecorder.SnapshotProductTtlPolicies(product), conflicts);
                return;
            }

            var sensor = _cache.GetSensor(image.NodeId);

            if (sensor is null)
            {
                conflicts.Add($"{image.Path}: sensor deleted after the journaled write");
                return;
            }

            AddListConflicts(image.Path, image.AfterPolicies,
                ChangeSetRecorder.SnapshotDataPolicies(sensor), conflicts);
            AddListConflicts(image.Path, image.AfterTtlPolicies,
                ChangeSetRecorder.SnapshotSensorTtlPolicies(sensor), conflicts);
        }

        private static void AddListConflicts(string path, List<PolicyEntity> afterImage, List<PolicyEntity> live,
            List<string> conflicts)
        {
            foreach (var line in PolicyImageComparer.DescribeListDiff(afterImage, live))
                conflicts.Add($"{path}: {line}");
        }

        private static bool TemplateRowsEqual(AlertTemplateEntity expected, AlertTemplateEntity current)
        {
            if ((expected is null) != (current is null))
                return false;

            if (expected is null || current is null)
                return true;

            return expected.Name == current.Name &&
                   expected.FolderId == current.FolderId &&
                   expected.SensorType == current.SensorType &&
                   (expected.Paths ?? []).OrderBy(p => p, StringComparer.Ordinal)
                       .SequenceEqual((current.Paths ?? []).OrderBy(p => p, StringComparer.Ordinal)) &&
                   PolicyImageComparer.ListsEqual(expected.Policies, current.Policies) &&
                   PolicyImageComparer.ListsEqual(expected.TTLPolicies, current.TTLPolicies) &&
                   IntervalsEqual(expected.TTLs, current.TTLs);
        }

        private static bool IntervalsEqual(List<TimeIntervalEntity> expected, List<TimeIntervalEntity> current)
        {
            expected ??= [];
            current ??= [];

            return expected.Count == current.Count && !expected.Where((interval, i) =>
                interval.Interval != current[i].Interval || interval.Ticks != current[i].Ticks).Any();
        }


        // === Visibility and authorization ===

        // A change set is VISIBLE when any of its entries touches an entity
        // the caller can see (#1570 story 13) — viewers get read-only sight.
        private bool EntryIsVisible(ChangeSetEntry entry, ClaimsPrincipal user) =>
            _authorization.AuthorizeRead(user, EntryResource(entry)) == ApiTokenAuthorization.Allowed;

        // The rollback pre-flight demands WRITE on every pending entry — the
        // same boundary the original write passed (story 12: another
        // principal's change set is rollbackable with write access).
        private bool EntryIsWritable(ChangeSetEntry entry, ClaimsPrincipal user) =>
            _authorization.AuthorizeWrite(user, EntryResource(entry)) == ApiTokenAuthorization.Allowed;

        private static ApiTokenResource EntryResource(ChangeSetEntry entry) =>
            entry.Kind switch
            {
                ChangeSetEntryKind.SensorDataPolicies or ChangeSetEntryKind.SensorTtlPolicies =>
                    new ApiTokenResource(ApiTokenResourceKind.Sensor, entry.EntityId),
                ChangeSetEntryKind.ProductTtlPolicies =>
                    new ApiTokenResource(ApiTokenResourceKind.Product, entry.EntityId),
                ChangeSetEntryKind.AlertTemplate when entry.ParentId is { } folder =>
                    new ApiTokenResource(ApiTokenResourceKind.Folder, folder),
                _ => ApiTokenResource.GlobalScope,
            };


        // === Rendering ===

        private static bool Matches(ChangeSetModel set, ChangeSetFilter filter) =>
            (filter.Author is null || string.Equals(set.Author, filter.Author, StringComparison.OrdinalIgnoreCase)) &&
            (filter.FromUtc is not { } from || set.CreatedAtUtc >= from) &&
            (filter.ToUtc is not { } to || set.CreatedAtUtc <= to) &&
            (filter.Status is null ||
             string.Equals(set.Status.ToString(), filter.Status.Replace("-", "").Replace("_", ""),
                 StringComparison.OrdinalIgnoreCase)) &&
            (filter.EntityId is not { } entity ||
             set.Entries.Any(e => e.EntityId == entity || (e.Fanout ?? []).Any(f => f.NodeId == entity)));

        private ChangeSetSummaryDto RenderSummary(ChangeSetModel set, ClaimsPrincipal user) =>
            new()
            {
                Id = set.Id.ToString(),
                Author = set.Author,
                AuthorType = set.AuthorType.ToString(),
                CreatedAtUtc = set.CreatedAtUtc,
                Status = StatusOf(set.Status),
                EntryCount = set.Entries.Count,
                RestoredEntryCount = set.Entries.Count(e => e.RestoredBy is not null),
                Entities = set.Entries.Where(e => EntryIsVisible(e, user)).Select(e => e.Path).ToList(),
            };

        private ChangeSetDetailsDto RenderDetails(ChangeSetModel set, ClaimsPrincipal user) =>
            new()
            {
                Summary = RenderSummary(set, user),
                Entries = set.Entries
                    .Where(e => EntryIsVisible(e, user))
                    .Select(e => new ChangeSetEntryDto
                    {
                        Sequence = e.Sequence,
                        Kind = KindOf(e),
                        Operation = OperationOf(e),
                        EntityId = e.EntityId.ToString(),
                        Path = e.Path,
                        Restored = e.RestoredBy is not null,
                        RestoredBy = e.RestoredBy?.ToString(),
                        Before = RenderPolicies(e.Before),
                        After = RenderPolicies(e.After),
                        BeforeTemplate = RenderJson(e.BeforeTemplate),
                        AfterTemplate = RenderJson(e.AfterTemplate),
                        BeforeSchedule = RenderJson(e.BeforeSchedule),
                        AfterSchedule = RenderJson(e.AfterSchedule),
                        Fanout = (e.Fanout ?? []).Where(i => EntryIsVisible(e, user)).Select(i => new NodeImageDto
                        {
                            NodeKind = i.NodeKind.ToString(),
                            Id = i.NodeId.ToString(),
                            Path = i.Path,
                            BeforePolicies = RenderPolicies(i.BeforePolicies),
                            AfterPolicies = RenderPolicies(i.AfterPolicies),
                            BeforeTtlPolicies = RenderPolicies(i.BeforeTtlPolicies),
                            AfterTtlPolicies = RenderPolicies(i.AfterTtlPolicies),
                        }).ToList(),
                    }).ToList(),
            };

        private static List<string> RenderPolicies(List<PolicyEntity> policies) =>
            (policies ?? []).Select(p => JsonSerializer.Serialize(p, ImageJson)).ToList();

        private static string RenderJson<T>(T entity) where T : class =>
            entity is null ? null : JsonSerializer.Serialize(entity, ImageJson);

        private static string StatusOf(ChangeSetStatus status) =>
            status switch
            {
                ChangeSetStatus.RolledBack => "rolledBack",
                ChangeSetStatus.Partial => "partial",
                _ => "applied",
            };

        private static string KindOf(ChangeSetEntry entry) =>
            WritePreviews.CamelOf(entry.Kind);

        private static string OperationOf(ChangeSetEntry entry) =>
            WritePreviews.CamelOf(entry.Operation);

        private static Dictionary<string, string> ConflictDetails(
            List<(ChangeSetEntry Entry, List<string> Conflicts)> conflicts) =>
            conflicts.Where(pair => pair.Conflicts.Count > 0)
                .ToDictionary(pair => pair.Entry.Path,
                    pair => string.Join("; ", pair.Conflicts),
                    StringComparer.Ordinal);


        // === Snapshot capture for the rollback's own journal entries ===

        private InitiatorInfo AuthorOf(ClaimsPrincipal user)
        {
            var ownerClaim = user?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value;
            var owner = Guid.TryParse(ownerClaim, out var ownerId) ? _users[ownerId] : null;

            return InitiatorInfo.AsUser(owner?.Name ?? "api-token");
        }

        private List<PolicyEntity> CaptureSensorData(Guid sensorId) =>
            _cache.GetSensor(sensorId) is { } sensor ? ChangeSetRecorder.SnapshotDataPolicies(sensor) : null;

        private List<PolicyEntity> CaptureSensorTtl(Guid sensorId) =>
            _cache.GetSensor(sensorId) is { } sensor ? ChangeSetRecorder.SnapshotSensorTtlPolicies(sensor) : null;

        private List<PolicyEntity> CaptureProductTtl(Guid productId) =>
            _cache.TryGetProduct(productId, out var product) && product is not null
                ? ChangeSetRecorder.SnapshotProductTtlPolicies(product)
                : null;

        private void RecordRollbackEntry(Guid rollbackChangeSetId, InitiatorInfo author, ChangeSetEntry original,
            List<PolicyEntity> before = null, List<PolicyEntity> after = null,
            AlertTemplateEntity beforeTemplate = null, AlertTemplateEntity afterTemplate = null,
            AlertScheduleEntity beforeSchedule = null, AlertScheduleEntity afterSchedule = null)
        {
            // The rollback entry mirrors the original entry's kind and entity
            // with the INVERSE operation, so a redo (rollback of the
            // rollback) restores the rollback's own before-images.
            var operation = original.Operation switch
            {
                ChangeSetOperation.Create => ChangeSetOperation.Delete,
                ChangeSetOperation.Delete => ChangeSetOperation.Create,
                _ => ChangeSetOperation.Update,
            };

            _recorder.Record(rollbackChangeSetId, author, new ChangeSetEntry
            {
                Kind = original.Kind,
                Operation = operation,
                EntityId = original.EntityId,
                ParentId = original.ParentId,
                Path = original.Path,
                Before = before,
                After = after,
                BeforeTemplate = beforeTemplate,
                AfterTemplate = afterTemplate,
                BeforeSchedule = beforeSchedule,
                AfterSchedule = afterSchedule,
            });
        }
    }

}
