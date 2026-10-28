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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
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
    /// <summary>
    /// Per-entry sight/write decisions of the undo-journal surface. The
    /// default is the token evaluator; the web UI supplies a cookie-`User`
    /// implementation (the evaluator only resolves token principals).
    /// </summary>
    public interface IChangeSetAccess
    {
        bool CanRead(ChangeSetEntry entry);

        bool CanWrite(ChangeSetEntry entry);

        // A fan-out image's node (sensor or product of a template apply or a
        // schedule detach): a viewer who sees the node sees the change set
        // touching it (story 13), even when the entry itself (a folder-bound
        // template) is outside the viewer's sight.
        bool CanReadNode(NodePolicyImage image);

        // A fan-out node's WRITE decision for the rollback pre-flight: the
        // restores re-authorize per node, so a caller who cannot write every
        // touched node would apply half a fan-out before failing — refuse
        // such a rollback up front instead.
        bool CanWriteNode(NodePolicyImage image);

        // The per-RESOURCE write decision the compensating writes themselves
        // authorize through: the restore paths decide per node (sensor,
        // product, folder, global), not per change-set entry. The token
        // default delegates to the evaluator; the cookie twin maps the web
        // user's role predicates onto the same resources.
        bool CanWrite(ApiTokenResource resource);
    }


    // The controller-facing contract of the undo-journal surface: mocked in
    // the REST contract tests, implemented by ChangeSetAdministrationService.
    public interface IChangeSetAdministrationService
    {
        PolicyWriteResult<List<ChangeSetSummaryDto>> ListChangeSets(ClaimsPrincipal user, ChangeSetFilter filter,
            IChangeSetAccess access = null);

        PolicyWriteResult<ChangeSetDetailsDto> GetChangeSet(Guid id, ClaimsPrincipal user,
            IChangeSetAccess access = null);

        Task<PolicyWriteResult<RollbackResultDto>> RollbackAsync(Guid id, ClaimsPrincipal user,
            bool force = false, bool dryRun = false, IChangeSetAccess access = null);
    }


    public sealed class ChangeSetAdministrationService : IChangeSetAdministrationService
    {
        private const int MaxLimit = 200;

        private static readonly JsonSerializerOptions ImageJson = new()
        {
            WriteIndented = true,
        };

        // One gate per change-set id: concurrent rollbacks of the same set
        // serialize here, so the second finds the set RolledBack instead of
        // replaying every entry and journaling an orphan rollback change set
        // (whose redo would re-apply writes nobody intended). Never removed —
        // bounded by the number of ids a server ever rolls back.
        private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> _rollbackGates = new();

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
            ChangeSetFilter filter, IChangeSetAccess access = null)
        {
            filter ??= new ChangeSetFilter();
            access = AccessOf(user, access);

            var limit = Math.Clamp(filter.Limit <= 0 ? 50 : filter.Limit, 1, MaxLimit);
            var offset = Math.Max(filter.Offset, 0);

            // Page BEFORE rendering: the UI reloads this list on every page
            // view, and a summary is built per visible set — the offset and
            // limit bound the rendering work too. The entityId filter sees
            // only the caller-visible entries: matching hidden ones would
            // leak their existence through the filter's selectivity.
            var page = _journal.GetAll()
                .Where(set => set.Entries.Any(e => access.CanRead(e) || HasVisibleNode(e, access)))
                .Where(set => Matches(set, filter, set.Entries.Where(access.CanRead).ToList()))
                .Skip(offset)
                .Take(limit)
                .Select(set => RenderSummary(set, access))
                .ToList();

            return PolicyWriteResult<List<ChangeSetSummaryDto>>.Ok(page);
        }

        public PolicyWriteResult<ChangeSetDetailsDto> GetChangeSet(Guid id, ClaimsPrincipal user,
            IChangeSetAccess access = null)
        {
            access = AccessOf(user, access);
            var set = _journal.Get(id);

            // Anti-enumeration: an unknown id and an invisible change set
            // answer the same 404.
            if (set is null || set.Entries.All(e => !access.CanRead(e) && !HasVisibleNode(e, access)))
                return PolicyWriteResult<ChangeSetDetailsDto>.Fail(PolicyWriteOutcome.NotFound);

            return PolicyWriteResult<ChangeSetDetailsDto>.Ok(RenderDetails(set, access));
        }


        // === Rollback ===

        public async Task<PolicyWriteResult<RollbackResultDto>> RollbackAsync(Guid id, ClaimsPrincipal user,
            bool force = false, bool dryRun = false, IChangeSetAccess access = null)
        {
            access = AccessOf(user, access);
            var set = _journal.Get(id);

            if (set is null || set.Entries.All(e => !access.CanRead(e) && !HasVisibleNode(e, access)))
                return PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.NotFound);

            var gate = _rollbackGates.GetOrAdd(id, _ => new SemaphoreSlim(1, 1));

            await gate.WaitAsync();
            try
            {
                // Re-read under the gate: a concurrent rollback of this id may
                // have finished while this call waited for the gate — and a
                // concurrent entity deletion may have purged the set entirely
                // (an entry-less change set is removed), so the re-read can
                // answer null where the pre-gate read did not.
                set = _journal.Get(id);

                if (set is null)
                    return PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.NotFound);

                if (set.Status == ChangeSetStatus.RolledBack)
                    return PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.Conflict,
                        message: "This journal record's request was already rolled back. Roll back the ROLLBACK's journal record (the same entity, kind-tagged) to redo it.");

                var pending = set.Entries.Where(e => e.RestoredBy is null).OrderBy(e => e.Sequence).ToList();

                // Pre-flight: authorization on every pending entry (the same
                // boundaries the original writes passed), then on every
                // FAN-OUT NODE — the restores re-authorize per node, so a
                // caller who cannot write one node would otherwise apply half
                // a fan-out before the per-node authorization fails.
                var denied = pending.FirstOrDefault(e => !access.CanWrite(e));

                if (denied is not null)
                    return PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.Forbidden,
                        message: PolicyWriteResult.WriteDeniedMessage);

                var deniedNode = pending.SelectMany(e => e.Fanout ?? [])
                    .FirstOrDefault(image => !access.CanWriteNode(image));

                if (deniedNode is not null)
                    return PolicyWriteResult<RollbackResultDto>.Fail(PolicyWriteOutcome.Forbidden,
                        message: PolicyWriteResult.WriteDeniedMessage);

                // Then the content comparison: each entry's after-image
                // against the next pending write's before-image (a grouped
                // change set may write one list twice), live state for the
                // latest pending write on each target.
                var reports = new List<RollbackEntryReportDto>();
                var conflicts = pending
                    .Select(e => (Entry: e, Conflicts: CompareWithChain(e, pending)))
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
                var landedAny = false;

                foreach (var entry in pending.OrderByDescending(e => e.Sequence))
                {
                    // A write may land on an entity between the pre-flight
                    // comparison and this restore (the gate serializes
                    // ROLLBACKS, not writes) — re-check the live content and
                    // fail the entry instead of silently overwriting it.
                    if (!force && CompareWithLive(entry).Count > 0)
                    {
                        failures++;

                        var stale = reports.First(r => r.Sequence == entry.Sequence);

                        reports.Remove(stale);
                        reports.Add(stale with
                        {
                            Status = "failed",
                            Message = "the entity changed after the pre-flight check — retry the rollback",
                        });

                        continue;
                    }

                    var (failure, landed) = await RestoreEntryAsync(entry, user, access, rollbackChangeSetId, author);

                    // A partially-failed entry may still have journaled its
                    // restored half (fan-out parity) — the redo handle is
                    // honest as soon as ANY rollback entry landed.
                    landedAny |= landed;

                    if (failure is not null)
                    {
                        failures++;

                        var report = reports.First(r => r.Sequence == entry.Sequence);

                        reports.Remove(report);
                        reports.Add(report with { Status = "failed", Message = failure });
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

                // The redo handle rides the response ONLY when at least one
                // rollback entry actually landed in the journal — the write
                // echoes' own rule (never hand out an id that holds nothing).
                return PolicyWriteResult<RollbackResultDto>.Ok(new RollbackResultDto
                {
                    ChangeSetId = id.ToString(),
                    DryRun = false,
                    Forced = force,
                    Status = completed ? "completed" : "partial",
                    Message = failures != 0
                        ? $"{failures} entr{(failures == 1 ? "y" : "ies")} could not be restored — see the per-entry reports and retry."
                        : landedAny
                            ? "Rolled back; the rollback's own journal record (same entity) is the redo handle — roll it back to re-apply."
                            : "Rolled back.",
                    RollbackChangeSetId = landedAny ? rollbackChangeSetId.ToString() : null,
                    Entries = reports,
                });
            }
            finally
            {
                gate.Release();
            }
        }


        // === Restore execution ===

        // One entry's compensating writes; returns the failure message (null
        // on success) and whether the rollback's own journal entry for it
        // actually LANDED (before = state at rollback time, after = restored
        // state — the redo path needs those images). The caller's access
        // rides every restore — the authorization seam the web UI needs.
        private async Task<(string Failure, bool Landed)> RestoreEntryAsync(ChangeSetEntry entry,
            ClaimsPrincipal user, IChangeSetAccess access, Guid rollbackChangeSetId, InitiatorInfo author)
        {
            switch (entry.Kind)
            {
                case ChangeSetEntryKind.SensorDataPolicies:
                    return await RestoreSensorListAsync(entry, data: entry.Before, ttl: null,
                        user, access, rollbackChangeSetId, author);

                case ChangeSetEntryKind.SensorTtlPolicies:
                    return await RestoreSensorListAsync(entry, data: null, ttl: entry.Before,
                        user, access, rollbackChangeSetId, author);

                case ChangeSetEntryKind.ProductTtlPolicies:
                {
                    var before = CaptureProductTtl(entry.EntityId);

                    var result = await _policies.RestoreProductTtlAsync(entry.EntityId, entry.Before, user, access);

                    if (!result.Success)
                        return (result.Failure.Message, false);

                    var landed = RecordRollbackEntry(rollbackChangeSetId, author, entry, before,
                        CaptureProductTtl(entry.EntityId));

                    return (null, landed);
                }

                case ChangeSetEntryKind.AlertTemplate:
                    return await RestoreTemplateAsync(entry, user, access, rollbackChangeSetId, author);

                case ChangeSetEntryKind.AlertSchedule:
                    return await RestoreScheduleAsync(entry, user, access, rollbackChangeSetId, author);

                default:
                    return ($"Unknown entry kind {entry.Kind}.", false);
            }
        }

        private async Task<(string Failure, bool Landed)> RestoreSensorListAsync(ChangeSetEntry entry,
            List<PolicyEntity> data, List<PolicyEntity> ttl, ClaimsPrincipal user, IChangeSetAccess access,
            Guid rollbackChangeSetId, InitiatorInfo author)
        {
            // Capture the list THIS entry owns (kind decides data vs TTL) —
            // the rollback's own before-image.
            var before = data is not null ? CaptureSensorData(entry.EntityId) : CaptureSensorTtl(entry.EntityId);

            var result = await _policies.RestoreSensorListsAsync(entry.EntityId, data, ttl, user, access);

            if (!result.Success)
                return (result.Failure.Message, false);

            var after = data is not null ? CaptureSensorData(entry.EntityId) : CaptureSensorTtl(entry.EntityId);

            var landed = RecordRollbackEntry(rollbackChangeSetId, author, entry, before, after);

            return (null, landed);
        }

        private async Task<(string Failure, bool Landed)> RestoreTemplateAsync(ChangeSetEntry entry,
            ClaimsPrincipal user, IChangeSetAccess access, Guid rollbackChangeSetId, InitiatorInfo author)
        {
            var beforeTemplate = _cache.GetAlertTemplate(entry.EntityId)?.ToEntity();

            // The template row first: a CREATE is compensated by the delete
            // (which strips its policies); an UPDATE or DELETE is compensated
            // by restoring the recorded row and re-running its fan-out.
            var result = entry.Operation == ChangeSetOperation.Create
                ? await _templates.RemoveTemplateCoreAsync(entry.EntityId, user, access)
                : await _templates.RestoreTemplateEntityAsync(entry.BeforeTemplate, user, access);

            if (!result.Success)
                return (result.Failure.Message, false);

            // Then the per-sensor exact restores: the fan-out images force
            // every touched sensor to its recorded before-list regardless of
            // what the template machinery just did. Each restored node's
            // state-at-rollback and restored lists ride the rollback's own
            // entry, so the redo path sees the same per-node images.
            var rollbackFanout = new List<NodePolicyImage>();

            foreach (var image in entry.Fanout ?? [])
            {
                PolicyWriteResult nodeResult;
                NodePolicyImage rollbackImage;

                if (image.NodeKind == ChangeSetNodeKind.Product)
                {
                    var beforeTtl = CaptureProductTtl(image.NodeId);

                    nodeResult = await _policies.RestoreProductTtlAsync(image.NodeId, image.BeforeTtlPolicies,
                        user, access);
                    rollbackImage = RollbackProductImage(image, beforeTtl);
                }
                else
                {
                    var beforeData = CaptureSensorData(image.NodeId);
                    var beforeTtl = CaptureSensorTtl(image.NodeId);

                    nodeResult = await _policies.RestoreSensorListsAsync(image.NodeId,
                        image.BeforePolicies, image.BeforeTtlPolicies, user, access);
                    rollbackImage = RollbackSensorImage(image, beforeData, beforeTtl);
                }

                if (!nodeResult.Success)
                {
                    // The row and the nodes before this one are ALREADY
                    // restored — journal them (redo parity): a retry without
                    // this record would have no undo point for the applied
                    // half. The entry stays pending; the set reports partial.
                    var journaled = RecordRollbackEntry(rollbackChangeSetId, author, entry,
                        beforeTemplate: beforeTemplate,
                        afterTemplate: _cache.GetAlertTemplate(entry.EntityId)?.ToEntity(),
                        fanout: rollbackFanout);

                    return ($"Sensor list restore failed on {image.Path}: {nodeResult.Failure.Message}", journaled);
                }

                rollbackFanout.Add(rollbackImage);
            }

            var landed = RecordRollbackEntry(rollbackChangeSetId, author, entry,
                beforeTemplate: beforeTemplate, afterTemplate: _cache.GetAlertTemplate(entry.EntityId)?.ToEntity(),
                fanout: rollbackFanout);

            return (null, landed);
        }

        private NodePolicyImage RollbackSensorImage(NodePolicyImage original,
            List<PolicyEntity> beforeData, List<PolicyEntity> beforeTtl)
        {
            var sensor = _cache.GetSensor(original.NodeId);

            return new NodePolicyImage
            {
                NodeKind = original.NodeKind,
                NodeId = original.NodeId,
                RootProductId = original.RootProductId,
                Path = original.Path,
                BeforePolicies = beforeData,
                AfterPolicies = sensor is null ? null : CaptureSensorData(original.NodeId),
                BeforeTtlPolicies = beforeTtl,
                AfterTtlPolicies = sensor is null ? null : CaptureSensorTtl(original.NodeId),
            };
        }

        private NodePolicyImage RollbackProductImage(NodePolicyImage original, List<PolicyEntity> beforeTtl) =>
            new()
            {
                NodeKind = original.NodeKind,
                NodeId = original.NodeId,
                RootProductId = original.RootProductId,
                Path = original.Path,
                BeforeTtlPolicies = beforeTtl,
                AfterTtlPolicies = CaptureProductTtl(original.NodeId),
            };

        private async Task<(string Failure, bool Landed)> RestoreScheduleAsync(ChangeSetEntry entry,
            ClaimsPrincipal user, IChangeSetAccess access, Guid rollbackChangeSetId, InitiatorInfo author)
        {
            var beforeSchedule = _schedules.GetSchedule(entry.EntityId)?.ToEntity();

            var result = entry.Operation == ChangeSetOperation.Create
                ? await _scheduleWrites.DeleteScheduleCoreAsync(entry.EntityId, user, access)
                : _scheduleWrites.RestoreScheduleEntity(entry.BeforeSchedule, user, access);

            if (!result.Success)
                return (result.Failure.Message, false);

            // The detached template rows FIRST (the template twin's order):
            // RestoreTemplateEntityAsync re-runs each template's fan-out, so
            // restoring them AFTER the per-node loop would silently overwrite
            // the exact recorded lists with the template engine's output and
            // leave those writes out of the rollback's own journal entry.
            foreach (var templateEntity in entry.BeforeTemplates ?? [])
            {
                var templateResult = await _templates.RestoreTemplateEntityAsync(templateEntity, user, access);

                if (!templateResult.Success)
                    return ($"Template restore failed: {templateResult.Failure.Message}", false);
            }

            // The detach fan-out: the per-node exact restores, forcing every
            // touched node to its recorded before-list regardless of what the
            // template re-runs just did; each node's rollback images ride the
            // rollback's own entry.
            var scheduleRollbackFanout = new List<NodePolicyImage>();

            foreach (var image in entry.Fanout ?? [])
            {
                PolicyWriteResult fanoutResult;
                NodePolicyImage rollbackImage;

                if (image.NodeKind == ChangeSetNodeKind.Product)
                {
                    var beforeTtl = CaptureProductTtl(image.NodeId);

                    fanoutResult = await _policies.RestoreProductTtlAsync(image.NodeId, image.BeforeTtlPolicies,
                        user, access);
                    rollbackImage = RollbackProductImage(image, beforeTtl);
                }
                else
                {
                    var beforeData = CaptureSensorData(image.NodeId);
                    var beforeTtl = CaptureSensorTtl(image.NodeId);

                    fanoutResult = await _policies.RestoreSensorListsAsync(image.NodeId, image.BeforePolicies,
                        image.BeforeTtlPolicies, user, access);
                    rollbackImage = RollbackSensorImage(image, beforeData, beforeTtl);
                }

                if (!fanoutResult.Success)
                {
                    // Journal the already-restored half before failing (the
                    // template twin's rule): the retry needs an undo point
                    // for everything this attempt applied.
                    var journaled = RecordRollbackEntry(rollbackChangeSetId, author, entry,
                        beforeSchedule: beforeSchedule,
                        afterSchedule: _schedules.GetSchedule(entry.EntityId)?.ToEntity(),
                        fanout: scheduleRollbackFanout);

                    return ($"Detach restore failed on {image.Path}: {fanoutResult.Failure.Message}", journaled);
                }

                scheduleRollbackFanout.Add(rollbackImage);
            }

            var landed = RecordRollbackEntry(rollbackChangeSetId, author, entry,
                beforeSchedule: beforeSchedule, afterSchedule: _schedules.GetSchedule(entry.EntityId)?.ToEntity(),
                fanout: scheduleRollbackFanout);

            return (null, landed);
        }


        // === Conflict detection (content comparison, #1570) ===

        // A comparison target: one journalable list or row an entry touches.
        // A grouped change set may write the SAME target twice (a plan's two
        // edits of one sensor, a template apply plus a manual policy) — the
        // conflict rule therefore compares an entry's after-image against the
        // NEXT pending write's before-image, and only the LATEST pending
        // write on each target against live state. Comparing every entry
        // against live state would flag the set's own later writes as
        // conflicts and train callers to force where nothing intervened.
        private enum ComparisonArea { SensorData, SensorTtl, ProductTtl, TemplateRow, ScheduleRow }

        private readonly record struct ComparisonTarget(ComparisonArea Area, Guid Id);

        private List<string> CompareWithChain(ChangeSetEntry entry, List<ChangeSetEntry> pending)
        {
            var conflicts = new List<string>();

            foreach (var (target, path) in TargetsOf(entry))
            {
                var successor = pending
                    .Where(e => e.Sequence > entry.Sequence && TargetsOf(e).Any(pair => pair.Target == target))
                    .MinBy(e => e.Sequence);

                if (successor is not null)
                {
                    CompareTarget(target, path, afterEntry: entry, baselineEntry: successor, conflicts);
                    continue;
                }

                CompareWithLive(target, path, entry, conflicts);
            }

            return conflicts;
        }

        // The live-only twin (no chain): the mid-rollback re-check just
        // before an entry restores — the restores of newer entries have
        // already rewound live state to this entry's successor images.
        private List<string> CompareWithLive(ChangeSetEntry entry)
        {
            var conflicts = new List<string>();

            foreach (var (target, path) in TargetsOf(entry))
                CompareWithLive(target, path, entry, conflicts);

            return conflicts;
        }

        private static IEnumerable<(ComparisonTarget Target, string Path)> TargetsOf(ChangeSetEntry entry)
        {
            switch (entry.Kind)
            {
                case ChangeSetEntryKind.SensorDataPolicies:
                    yield return (new ComparisonTarget(ComparisonArea.SensorData, entry.EntityId), entry.Path);
                    break;

                case ChangeSetEntryKind.SensorTtlPolicies:
                    yield return (new ComparisonTarget(ComparisonArea.SensorTtl, entry.EntityId), entry.Path);
                    break;

                case ChangeSetEntryKind.ProductTtlPolicies:
                    yield return (new ComparisonTarget(ComparisonArea.ProductTtl, entry.EntityId), entry.Path);
                    break;

                case ChangeSetEntryKind.AlertTemplate:
                {
                    yield return (new ComparisonTarget(ComparisonArea.TemplateRow, entry.EntityId), entry.Path);

                    foreach (var pair in NodeTargets(entry.Fanout))
                        yield return pair;

                    break;
                }

                case ChangeSetEntryKind.AlertSchedule:
                {
                    yield return (new ComparisonTarget(ComparisonArea.ScheduleRow, entry.EntityId), entry.Path);

                    foreach (var pair in NodeTargets(entry.Fanout))
                        yield return pair;

                    foreach (var template in entry.BeforeTemplates ?? [])
                        yield return (new ComparisonTarget(ComparisonArea.TemplateRow, new Guid(template.Id)),
                            template.Name);

                    break;
                }
            }
        }

        private static IEnumerable<(ComparisonTarget Target, string Path)> NodeTargets(List<NodePolicyImage> fanout)
        {
            foreach (var image in fanout ?? [])
            {
                if (image.NodeKind == ChangeSetNodeKind.Product)
                    yield return (new ComparisonTarget(ComparisonArea.ProductTtl, image.NodeId), image.Path);
                else
                {
                    yield return (new ComparisonTarget(ComparisonArea.SensorData, image.NodeId), image.Path);
                    yield return (new ComparisonTarget(ComparisonArea.SensorTtl, image.NodeId), image.Path);
                }
            }
        }

        // The successor-baseline comparison: the entry's after-image against
        // the NEXT pending write's before-image on the same target.
        private void CompareTarget(ComparisonTarget target, string path, ChangeSetEntry afterEntry,
            ChangeSetEntry baselineEntry, List<string> conflicts)
        {
            switch (target.Area)
            {
                case ComparisonArea.SensorData:
                case ComparisonArea.SensorTtl:
                case ComparisonArea.ProductTtl:
                    AddListConflicts(path,
                        ListImageOf(afterEntry, target, before: false),
                        ListImageOf(baselineEntry, target, before: true),
                        conflicts);
                    break;

                case ComparisonArea.TemplateRow:
                    if (!TemplateRowsEqual(TemplateImageOf(afterEntry, target.Id, before: false),
                            TemplateImageOf(baselineEntry, target.Id, before: true)))
                        conflicts.Add($"{path}: template changed after the journaled write");
                    break;

                case ComparisonArea.ScheduleRow:
                    if (!ScheduleRowsEqual(ScheduleImageOf(afterEntry, before: false),
                            ScheduleImageOf(baselineEntry, before: true)))
                        conflicts.Add($"{path}: schedule changed after the journaled write");
                    break;
            }
        }

        private void CompareWithLive(ComparisonTarget target, string path, ChangeSetEntry entry,
            List<string> conflicts)
        {
            switch (target.Area)
            {
                case ComparisonArea.SensorData:
                case ComparisonArea.SensorTtl:
                {
                    if (_cache.GetSensor(target.Id) is not { } sensor)
                    {
                        conflicts.Add($"{path}: sensor deleted after the journaled write");
                        break;
                    }

                    var live = target.Area == ComparisonArea.SensorData
                        ? ChangeSetRecorder.SnapshotDataPolicies(sensor)
                        : ChangeSetRecorder.SnapshotSensorTtlPolicies(sensor);

                    AddListConflicts(path, ListImageOf(entry, target, before: false), live, conflicts);
                    break;
                }

                case ComparisonArea.ProductTtl:
                {
                    if (!_cache.TryGetProduct(target.Id, out var product) || product is null)
                    {
                        conflicts.Add($"{path}: product deleted after the journaled write");
                        break;
                    }

                    AddListConflicts(path, ListImageOf(entry, target, before: false),
                        ChangeSetRecorder.SnapshotProductTtlPolicies(product), conflicts);
                    break;
                }

                case ComparisonArea.TemplateRow:
                    if (!TemplateRowsEqual(TemplateImageOf(entry, target.Id, before: false),
                            _cache.GetAlertTemplate(target.Id)?.ToEntity()))
                        conflicts.Add($"{path}: template changed after the journaled write");
                    break;

                case ComparisonArea.ScheduleRow:
                    if (!ScheduleRowsEqual(ScheduleImageOf(entry, before: false),
                            _schedules.GetSchedule(target.Id)?.ToEntity()))
                        conflicts.Add($"{path}: schedule changed after the journaled write");
                    break;
            }
        }

        // The recorded list image of one target: the entry's own Before/After
        // for a policy list write, the fan-out node image for a template or
        // schedule entry.
        private static List<PolicyEntity> ListImageOf(ChangeSetEntry entry, ComparisonTarget target, bool before) =>
            entry.Kind switch
            {
                ChangeSetEntryKind.SensorDataPolicies when target.Area == ComparisonArea.SensorData =>
                    before ? entry.Before : entry.After,
                ChangeSetEntryKind.SensorTtlPolicies when target.Area == ComparisonArea.SensorTtl =>
                    before ? entry.Before : entry.After,
                ChangeSetEntryKind.ProductTtlPolicies when target.Area == ComparisonArea.ProductTtl =>
                    before ? entry.Before : entry.After,
                _ => (entry.Fanout ?? []).FirstOrDefault(image => image.NodeId == target.Id) switch
                {
                    { } image when target.Area == ComparisonArea.SensorData =>
                        before ? image.BeforePolicies : image.AfterPolicies,
                    { } image => before ? image.BeforeTtlPolicies : image.AfterTtlPolicies,
                    _ => null,
                },
            };

        private static AlertTemplateEntity TemplateImageOf(ChangeSetEntry entry, Guid templateId, bool before) =>
            entry.Kind == ChangeSetEntryKind.AlertTemplate
                ? before ? entry.BeforeTemplate : entry.AfterTemplate
                : (before ? entry.BeforeTemplates : entry.AfterTemplates)?.FirstOrDefault(
                    template => new Guid(template.Id) == templateId);

        private static AlertScheduleEntity ScheduleImageOf(ChangeSetEntry entry, bool before) =>
            before ? entry.BeforeSchedule : entry.AfterSchedule;

        private static bool ScheduleRowsEqual(AlertScheduleEntity expected, AlertScheduleEntity current) =>
            (expected is null) == (current is null) &&
            (expected is null || current is null ||
             (expected.Name == current.Name && expected.Timezone == current.Timezone &&
              expected.Schedule == current.Schedule));

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

        private static bool HasVisibleNode(ChangeSetEntry entry, IChangeSetAccess access) =>
            (entry.Fanout ?? []).Any(access.CanReadNode);

        // The default access: the token evaluator over the caller's
        // HsmApiToken principal. The web UI overrides with its cookie-`User`
        // implementation — the evaluator resolves token principals only.
        private IChangeSetAccess AccessOf(ClaimsPrincipal user, IChangeSetAccess access) =>
            access ?? new TokenAccess(_authorization, user);

        private sealed class TokenAccess : IChangeSetAccess
        {
            private readonly IApiTokenAuthorizationService _authorization;
            private readonly ClaimsPrincipal _user;

            public TokenAccess(IApiTokenAuthorizationService authorization, ClaimsPrincipal user)
            {
                _authorization = authorization;
                _user = user;
            }

            // A change set is VISIBLE when any of its entries touches an
            // entity the caller can see (#1570 story 13) — viewers get
            // read-only sight.
            public bool CanRead(ChangeSetEntry entry) =>
                _authorization.AuthorizeRead(_user, EntryResource(entry)) == ApiTokenAuthorization.Allowed;

            // The rollback pre-flight demands WRITE on every pending entry —
            // the same boundary the original write passed (story 12: another
            // principal's change set is rollbackable with write access).
            public bool CanWrite(ChangeSetEntry entry) =>
                _authorization.AuthorizeWrite(_user, EntryResource(entry)) == ApiTokenAuthorization.Allowed;

            public bool CanReadNode(NodePolicyImage image) =>
                _authorization.AuthorizeRead(_user,
                    new ApiTokenResource(ApiTokenResourceKind.Product, image.RootProductId)) ==
                ApiTokenAuthorization.Allowed;

            public bool CanWriteNode(NodePolicyImage image) =>
                _authorization.AuthorizeWrite(_user, image.NodeKind == ChangeSetNodeKind.Product
                    ? new ApiTokenResource(ApiTokenResourceKind.Product, image.NodeId)
                    : new ApiTokenResource(ApiTokenResourceKind.Sensor, image.NodeId)) ==
                ApiTokenAuthorization.Allowed;

            public bool CanWrite(ApiTokenResource resource) =>
                _authorization.AuthorizeWrite(_user, resource) == ApiTokenAuthorization.Allowed;
        }

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

        // The entityId filter matches only the caller-VISIBLE entries —
        // matching hidden ones would let a caller probe whether an invisible
        // entity was touched by a set they can see.
        private static bool Matches(ChangeSetModel set, ChangeSetFilter filter, List<ChangeSetEntry> visibleEntries) =>
            (filter.Author is null || string.Equals(set.Author, filter.Author, StringComparison.OrdinalIgnoreCase)) &&
            (filter.FromUtc is not { } from || set.CreatedAtUtc >= from) &&
            (filter.ToUtc is not { } to || set.CreatedAtUtc <= to) &&
            (filter.Status is null ||
             string.Equals(set.Status.ToString(), filter.Status.Replace("-", "").Replace("_", ""),
                 StringComparison.OrdinalIgnoreCase)) &&
            (filter.EntityId is not { } entity ||
             visibleEntries.Any(e => e.EntityId == entity || (e.Fanout ?? []).Any(f => f.NodeId == entity)));

        // The counts and the entities list see only the caller-visible
        // entries (the details view's own filter) — hidden entries must not
        // be observable through the arithmetic either.
        private ChangeSetSummaryDto RenderSummary(ChangeSetModel set, IChangeSetAccess access)
        {
            var visibleEntries = set.Entries.Where(access.CanRead).ToList();

            return new ChangeSetSummaryDto
            {
                Id = set.Id.ToString(),
                Author = set.Author,
                AuthorType = set.AuthorType.ToString(),
                CreatedAtUtc = set.CreatedAtUtc,
                Status = StatusOf(set.Status),
                EntryCount = visibleEntries.Count,
                RestoredEntryCount = visibleEntries.Count(e => e.RestoredBy is not null),
                Entities = visibleEntries.Select(e => e.Path).ToList(),
            };
        }

        private ChangeSetDetailsDto RenderDetails(ChangeSetModel set, IChangeSetAccess access) =>
            new()
            {
                Summary = RenderSummary(set, access),
                Entries = set.Entries
                    .Where(e => access.CanRead(e))
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
                        Fanout = (e.Fanout ?? []).Where(i => access.CanReadNode(i)).Select(i => new NodeImageDto
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

        // One change set can hold SEVERAL entries under one path (a sensor's
        // data and TTL lists journaled as separate writes) — the diff report
        // merges them per path instead of tripping ToDictionary's duplicate
        // key with a 500.
        private static Dictionary<string, string> ConflictDetails(
            List<(ChangeSetEntry Entry, List<string> Conflicts)> conflicts) =>
            conflicts.Where(pair => pair.Conflicts.Count > 0)
                .GroupBy(pair => pair.Entry.Path, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => string.Join("; ", group.SelectMany(pair => pair.Conflicts)),
                    StringComparer.Ordinal);


        // === Snapshot capture for the rollback's own journal entries ===

        private InitiatorInfo AuthorOf(ClaimsPrincipal user) =>
            ChangeSetRecorder.InitiatorOf(user, _users);

        private List<PolicyEntity> CaptureSensorData(Guid sensorId) =>
            _cache.GetSensor(sensorId) is { } sensor ? ChangeSetRecorder.SnapshotDataPolicies(sensor) : null;

        private List<PolicyEntity> CaptureSensorTtl(Guid sensorId) =>
            _cache.GetSensor(sensorId) is { } sensor ? ChangeSetRecorder.SnapshotSensorTtlPolicies(sensor) : null;

        private List<PolicyEntity> CaptureProductTtl(Guid productId) =>
            _cache.TryGetProduct(productId, out var product) && product is not null
                ? ChangeSetRecorder.SnapshotProductTtlPolicies(product)
                : null;

        private bool RecordRollbackEntry(Guid rollbackChangeSetId, InitiatorInfo author, ChangeSetEntry original,
            List<PolicyEntity> before = null, List<PolicyEntity> after = null,
            AlertTemplateEntity beforeTemplate = null, AlertTemplateEntity afterTemplate = null,
            AlertScheduleEntity beforeSchedule = null, AlertScheduleEntity afterSchedule = null,
            List<NodePolicyImage> fanout = null)
        {
            // The rollback entry mirrors the original entry's kind and entity
            // with the INVERSE operation, so a redo (rollback of the
            // rollback) restores the rollback's own before-images. The
            // recorder ALSO writes the rollback group's unified-journal
            // record (#1588) — the redo handle must be visible in the journal.
            var operation = original.Operation switch
            {
                ChangeSetOperation.Create => ChangeSetOperation.Delete,
                ChangeSetOperation.Delete => ChangeSetOperation.Create,
                _ => ChangeSetOperation.Update,
            };

            return _recorder.RecordRollback(rollbackChangeSetId, author, new ChangeSetEntry
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
                Fanout = fanout,
            });
        }
    }

}
