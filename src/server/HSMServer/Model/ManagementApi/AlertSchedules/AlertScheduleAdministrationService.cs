using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.TaskResult;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Model;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using ScheduleModel = HSMServer.Core.Model.Policies.AlertSchedule;

namespace HSMServer.Model.ManagementApi.AlertSchedules
{
    /// <summary>
    /// The write engine of the schedule CRUD — the schedules' twin of
    /// <see cref="PolicyAdministrationService"/> (#1500 pattern): transport-
    /// agnostic, returning <see cref="PolicyWriteResult{T}"/> so the REST
    /// controller and the MCP write tools render the same decision.
    /// </summary>
    // Authorization follows the direction fixed when the read surface landed
    // (management-api feature.md): the write must NOT reuse the caller-wide
    // read gate (CanSeeAnyBoundary — the read-side equivalent of "the UI
    // shows schedules to every logged-in user"), because writes at the
    // GLOBAL boundary are admin-only in the evaluator. The web UI gates
    // SavePartial/Remove with [AuthorizeIsAdmin] (a cookie-principal check);
    // the token-side equivalent is AuthorizeWrite(User, GlobalScope):
    // admin owner + read-write token, with the evaluator's split preserved —
    // a non-admin owner answers 404 (Global sight is admin-only, so a
    // non-admin learns nothing about the surface), an admin owner with a
    // read-only token answers 403 (the method backstop rejects read-only
    // tokens on unsafe methods before the action anyway).
    //
    // Semantics ported from the web controller (AlertSchedulesController):
    //  - name uniqueness is GLOBAL (exact, case-sensitive, excluding self);
    //  - the parser throws on bad YAML and on semantically invalid schedules
    //    (windows >= 1 min, non-overlapping, start < end, dates 2000..2100)
    //    — the message becomes the field-keyed 422 detail;
    //  - timezone validation is NEW server-side (the web UI relies on its
    //    dropdown; the API validates system timezone ids — IANA and Windows
    //    both resolve — through TimeZoneInfo, the same lookup AlertSchedule
    //    uses at evaluation, so anything accepted resolves at runtime);
    //  - delete detaches FIRST: on any incomplete detach the schedule
    //    SURVIVES (409) so the retry re-runs the idempotent detach over the
    //    survivors — deleting it would strand the surviving references
    //    permanently (#1409). The web Remove's unconditional-detach variant
    //    (no existence guard, cleaning rows the loader skipped) stays a
    //    cookie-UI capability; the API answers 404 for an unknown id.
    public sealed class AlertScheduleAdministrationService
    {
        public const int MaxNameLength = 200;

        private readonly IAlertScheduleProvider _schedules;
        private readonly ITreeValuesCache _cache;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly AlertReadService _reader;
        private readonly ChangeSetRecorder _changeSets;
        private readonly IUserManager _users;

        // Stateless YAML machinery (the web controller holds one the same way);
        // not DI-registered anywhere today.
        private readonly AlertScheduleParser _parser = new();

        // The name-uniqueness check and the save must run as ONE atomic step:
        // the service is Scoped, so the gate is STATIC — every request's
        // check-then-save pair executes under the same process-wide lock.
        // Without it two concurrent creates with one name both pass the check
        // and both persist (the web controller shares the gap, but scripted
        // REST/MCP clients that retry on timeout make the race realistic
        // here). Safe to hold across SaveSchedule: the provider takes only
        // its own inner lock, and nobody takes that lock and then ours.
        private static readonly object NameUniquenessGate = new();

        public AlertScheduleAdministrationService(IAlertScheduleProvider schedules, ITreeValuesCache cache,
            IApiTokenAuthorizationService authorization, AlertReadService reader, ChangeSetRecorder changeSets,
            IUserManager users)
        {
            _schedules = schedules;
            _cache = cache;
            _authorization = authorization;
            _reader = reader;
            _changeSets = changeSets;
            _users = users;
        }


        /// <summary>
        /// Create a schedule. The server generates the id (a client cannot
        /// choose one — the provider's Save is an upsert by id); the response
        /// echoes the stored schedule.
        /// </summary>
        public Task<PolicyWriteResult<AlertScheduleDto>> CreateScheduleAsync(ClaimsPrincipal user,
            AlertScheduleUpsertDto dto, ChangeSetWriteOptions write = null,
            ChangeSets.IChangeSetAccess access = null) =>
            Task.FromResult(Write(user, id: null, dto, write, access));

        /// <summary>
        /// Update a schedule (upsert semantics at a known id); the response
        /// echoes the stored schedule.
        /// </summary>
        public Task<PolicyWriteResult<AlertScheduleDto>> UpdateScheduleAsync(Guid id, ClaimsPrincipal user,
            AlertScheduleUpsertDto dto, ChangeSetWriteOptions write = null,
            ChangeSets.IChangeSetAccess access = null) =>
            Task.FromResult(Write(user, id, dto, write, access));

        /// <summary>
        /// Delete a schedule. Detaches the id from every referencing policy
        /// FIRST; on an incomplete detach the schedule survives and the answer
        /// is a Conflict (retry re-runs the idempotent detach).
        /// </summary>
        // Known limitation (accepted, round-2 review): a policy/template write
        // concurrently in flight can pass its scheduleId existence check while
        // this delete detaches, then persist a dangling reference — the
        // evaluator then treats the missing schedule as working time (#1409's
        // throttled error log is the only trace). Nothing serializes the two
        // write paths (the web UI has the same gap); closing it would mean a
        // shared gate inside the core cache update paths. See
        // management-api/feature.md.
        public async Task<PolicyWriteResult> DeleteScheduleAsync(Guid id, ClaimsPrincipal user,
            CancellationToken cancellationToken = default, ChangeSetWriteOptions write = null,
            ChangeSets.IChangeSetAccess access = null)
        {
            if (AuthorizeGlobalWrite(user, access) is { } denied)
                return PolicyWriteResult.Fail(denied.Outcome, message: denied.Message);

            if (_schedules.GetSchedule(id) is not { } schedule)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            // #1570 dry-run: report the nodes the detach would touch (sensors
            // and products referencing the schedule) plus the schedule row
            // itself; nothing is detached or deleted.
            if (write is { DryRun: true })
                return PolicyWriteResult.OkPreview(SchedulePreview(write, id, schedule.Name));

            var changeSetId = ChangeSetRecorder.ResolveChangeSetId(write);

            // #1570 before-images of the detach fan-out: every sensor whose
            // policies reference the schedule, every product with a TTL
            // reference, and every template row carrying the reference.
            var (sensors, products) = ScheduleReferencingNodes(id);
            var templates = ScheduleReferencingTemplates(id);
            var beforeSensorImages = CaptureSensorImages(sensors);
            var beforeProductTtls = products.ToDictionary(p => p.Id, ChangeSetRecorder.SnapshotProductTtlPolicies);
            var beforeTemplateEntities = templates.Select(t => t.ToEntity()).ToList();

            // Deliberately tied to the cancellation token like the web Remove:
            // an aborted request stops the DISPATCH of further detach work;
            // the schedule is not deleted either way (see the class comment).
            var deleted = await DeleteScheduleCoreAsync(id, user, access, cancellationToken);

            // The delete's journal entry: the row's before/after image plus
            // the per-node detach images (the fan-out afters are the LIVE
            // lists, so a PARTIAL detach journals exactly what was cleared).
            ChangeSetEntry DeleteEntry(bool scheduleDeleted) => new()
            {
                Kind = ChangeSetEntryKind.AlertSchedule,
                Operation = ChangeSetOperation.Delete,
                EntityId = id,
                ParentId = null,
                Path = schedule.Name,
                BeforeSchedule = schedule.ToEntity(),
                AfterSchedule = scheduleDeleted ? null : schedule.ToEntity(),
                Fanout =
                [
                    .. sensors.Select(s => SensorImage(s, beforeSensorImages[s.Id],
                        ChangeSetRecorder.SnapshotDataPolicies(s), ChangeSetRecorder.SnapshotSensorTtlPolicies(s))),
                    .. products.Select(p => ProductImage(p, beforeProductTtls[p.Id])),
                ],
                BeforeTemplates = beforeTemplateEntities,
                AfterTemplates = templates.Select(t => t.ToEntity()).ToList(),
            };

            if (!deleted.Success)
            {
                // #1570: an incomplete detach has already cleared SOME
                // references while the schedule survives — journal the
                // partial detach so the cleared references keep their undo
                // point; auth/NotFound failures changed nothing and journal
                // nothing.
                if (deleted.Failure.Outcome == PolicyWriteOutcome.Conflict)
                    _changeSets.Record(changeSetId, InitiatorOf(user, write), DeleteEntry(scheduleDeleted: false));

                return deleted;
            }

            _changeSets.Record(changeSetId, InitiatorOf(user, write), DeleteEntry(scheduleDeleted: true));

            return PolicyWriteResult.Ok();
        }


        // === Rollback restore paths (#1570) ===

        // Non-journaling delete core: the detach walk plus the gated remove,
        // no capture and no journal entry. The rollback engine calls this
        // when compensating a schedule CREATE.
        internal async Task<PolicyWriteResult> DeleteScheduleCoreAsync(Guid id, ClaimsPrincipal user,
            ChangeSets.IChangeSetAccess access = null, CancellationToken cancellationToken = default)
        {
            if (AuthorizeGlobalWrite(user, access) is { } denied)
                return PolicyWriteResult.Fail(denied.Outcome, message: denied.Message);

            if (_schedules.GetSchedule(id) is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            var detach = await _cache.DetachAlertScheduleFromPoliciesAsync(id, cancellationToken);

            if (!detach.IsOk)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict,
                    message:
                        "The schedule was not deleted — not every policy reference could be cleared. Delete it again to retry. " +
                        detach.Error);

            lock (NameUniquenessGate)
            {
                if (_schedules.GetSchedule(id) is null)
                    return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

                _schedules.DeleteSchedule(id);
            }

            return PolicyWriteResult.Ok();
        }

        // Restore a recorded schedule row VERBATIM (same id, recorded YAML).
        // The uniqueness gate still serializes with the write path; a name
        // collision with a NEWER schedule is not re-judged — the rollback's
        // contract is to restore the recorded configuration.
        internal PolicyWriteResult RestoreScheduleEntity(AlertScheduleEntity entity, ClaimsPrincipal user,
            ChangeSets.IChangeSetAccess access = null)
        {
            if (AuthorizeGlobalWrite(user, access) is { } denied)
                return PolicyWriteResult.Fail(denied.Outcome, message: denied.Message);

            ScheduleModel schedule;

            try
            {
                schedule = _parser.Parse(entity?.Schedule ?? string.Empty);
                schedule.Id = new Guid(entity.Id);
                schedule.Name = entity.Name;
                schedule.Timezone = entity.Timezone;
            }
            catch (Exception ex)
            {
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict,
                    message: $"The recorded schedule image could not be reconstructed: {ex.Message}");
            }

            lock (NameUniquenessGate)
            {
                _schedules.SaveSchedule(schedule);
            }

            return PolicyWriteResult.Ok();
        }


        private PolicyWriteResult<AlertScheduleDto> Write(ClaimsPrincipal user, Guid? id,
            AlertScheduleUpsertDto dto, ChangeSetWriteOptions write = null,
            ChangeSets.IChangeSetAccess access = null)
        {
            if (AuthorizeGlobalWrite(user, access) is { } denied)
                return PolicyWriteResult<AlertScheduleDto>.Fail(denied.Outcome, message: denied.Message);

            // Authorization precedes body validation and existence (#1500
            // rule): a body-shape error must not reveal anything to a caller
            // outside the surface's reach. On update, an unknown id is a
            // plain 404 for an entitled caller.
            //
            // A JSON-null body reaches here only from MCP (REST's
            // [ApiController] binding rejects it with a 400 before the
            // service runs): answer the field-keyed shape the tool contract
            // promises, never a NullReferenceException.
            if (dto is null)
                return PolicyWriteResult<AlertScheduleDto>.Fail(PolicyWriteOutcome.Invalid,
                    new Dictionary<string, string[]> { ["schedule"] = ["The schedule body is required."] });

            ScheduleModel schedule;
            var changeSetId = ChangeSetRecorder.ResolveChangeSetId(write);

            lock (NameUniquenessGate)
            {
                // The existence check lives INSIDE the gate (round-3 review):
                // SaveSchedule is an upsert, so an update that passed an
                // UNLOCKED check while a concurrent delete finished would
                // resurrect the deleted schedule. With the check and the save
                // under the same gate DeleteScheduleAsync takes around its
                // final remove, one of the two loses cleanly instead.
                var before = id is null ? null : _schedules.GetSchedule(id.Value);

                if (id is not null && before is null)
                    return PolicyWriteResult<AlertScheduleDto>.Fail(PolicyWriteOutcome.NotFound);

                if (!TryBuildValidatedSchedule(dto, id, out schedule, out var errors))
                    return PolicyWriteResult<AlertScheduleDto>.Fail(PolicyWriteOutcome.Invalid, errors);

                // #1570 dry-run: the body is valid; report the row the write
                // would create/replace, apply nothing.
                if (write is { DryRun: true })
                    return PolicyWriteResult<AlertScheduleDto>.OkPreview(WritePreviews.Create(
                        WritePreviews.CamelOf(ChangeSetEntryKind.AlertSchedule),
                        WritePreviews.CamelOf(id is null ? ChangeSetOperation.Create : ChangeSetOperation.Update),
                        schedule.Id, schedule.Name,
                        [new PreviewAffectedEntityDto { Id = schedule.Id.ToString(), Path = schedule.Name, Change = "save" }]));

                // The provider's Save is an idempotent upsert (cache + LevelDB row).
                _schedules.SaveSchedule(schedule);

                _changeSets.Record(changeSetId, InitiatorOf(user, write), new ChangeSetEntry
                {
                    Kind = ChangeSetEntryKind.AlertSchedule,
                    Operation = id is null ? ChangeSetOperation.Create : ChangeSetOperation.Update,
                    EntityId = schedule.Id,
                    ParentId = null,
                    Path = schedule.Name,
                    BeforeSchedule = before?.ToEntity(),
                    AfterSchedule = schedule.ToEntity(),
                });
            }

            // Echo the STORED schedule through the read mapping — sensor
            // references filtered to the owner's sight, exactly as GET does.
            return PolicyWriteResult<AlertScheduleDto>.Ok(_reader.RenderSchedule(schedule, user));
        }


        /// <summary>
        /// The web editor's save entry (#1588: the single write path): the
        /// form's PARSED schedule model (the view model validated the form)
        /// rides the same global-write authorization, name-uniqueness gate
        /// and undo journaling as the DTO path. A null id creates (the id is
        /// server-generated), a known id updates.
        /// </summary>
        public PolicyWriteResult<AlertScheduleDto> SaveSchedule(ClaimsPrincipal user, Guid? id,
            ScheduleModel schedule, ChangeSetWriteOptions write = null, ChangeSets.IChangeSetAccess access = null)
        {
            if (AuthorizeGlobalWrite(user, access) is { } denied)
                return PolicyWriteResult<AlertScheduleDto>.Fail(denied.Outcome, message: denied.Message);

            var changeSetId = ChangeSetRecorder.ResolveChangeSetId(write);

            lock (NameUniquenessGate)
            {
                var before = id is null ? null : _schedules.GetSchedule(id.Value);

                if (id is not null && before is null)
                    return PolicyWriteResult<AlertScheduleDto>.Fail(PolicyWriteOutcome.NotFound);

                if ((_schedules.GetAllSchedules() ?? []).Any(
                        existing => existing.Name == schedule.Name && existing.Id != id))
                    return PolicyWriteResult<AlertScheduleDto>.Fail(PolicyWriteOutcome.Invalid,
                        new Dictionary<string, string[]> { ["name"] = ["The schedule name must be unique."] });

                // The id is server-owned (the DTO path's rule): generated on
                // create, the known id on update.
                schedule.Id = id ?? Guid.NewGuid();

                _schedules.SaveSchedule(schedule);

                _changeSets.Record(changeSetId, InitiatorOf(user, write), new ChangeSetEntry
                {
                    Kind = ChangeSetEntryKind.AlertSchedule,
                    Operation = id is null ? ChangeSetOperation.Create : ChangeSetOperation.Update,
                    EntityId = schedule.Id,
                    ParentId = null,
                    Path = schedule.Name,
                    BeforeSchedule = before?.ToEntity(),
                    AfterSchedule = schedule.ToEntity(),
                });
            }

            return PolicyWriteResult<AlertScheduleDto>.Ok(_reader.RenderSchedule(schedule, user));
        }


        // Field validation accumulates across name/timezone/schedule — the
        // templates' structural pass shape — and the YAML parse runs inside a
        // try (the domain parser throws on legal-looking but invalid input)
        // whatever the other fields say, so a caller with several problems
        // learns them all in one 422.
        private bool TryBuildValidatedSchedule(AlertScheduleUpsertDto dto, Guid? id,
            out ScheduleModel schedule, out IDictionary<string, string[]> errors)
        {
            var errorMap = new Dictionary<string, List<string>>();

            if (string.IsNullOrWhiteSpace(dto.Name))
                errorMap.Add("name", ["The schedule name is required."]);
            else if (dto.Name.Length > MaxNameLength)
                errorMap.Add("name", [$"The schedule name must be at most {MaxNameLength} characters."]);
            else if ((_schedules.GetAllSchedules() ?? []).Any(
                     existing => existing.Name == dto.Name && existing.Id != id))
                errorMap.Add("name", ["The schedule name must be unique."]);

            if (string.IsNullOrWhiteSpace(dto.Timezone))
            {
                errorMap.Add("timezone", ["The timezone is required."]);
            }
            else
            {
                try
                {
                    TimeZoneInfo.FindSystemTimeZoneById(dto.Timezone);
                }
                catch (Exception)
                {
                    errorMap.Add("timezone",
                        [$"The timezone '{dto.Timezone}' is not a known system timezone id (IANA or Windows)."]);
                }
            }

            schedule = null;

            try
            {
                schedule = _parser.Parse(dto.Schedule ?? string.Empty);

                // The id is server-owned: generated on create, the route id on
                // update — never a client-chosen value. Assigned only on the
                // success path below.
                schedule.Id = id ?? Guid.NewGuid();
                schedule.Name = dto.Name;
                schedule.Timezone = dto.Timezone;
            }
            catch (Exception ex)
            {
                errorMap.Add("schedule", [ex.Message]);
            }

            errors = null;

            if (errorMap.Count == 0)
                return true;

            errors = errorMap.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            return false;
        }


        // The evaluator's decision at the GLOBAL boundary: admin owner +
        // read-write token. The 403/404 split is preserved verbatim — a
        // non-admin owner's Global sight is false, so the answer is the same
        // 404 as an unknown id (nothing about the surface's existence leaks).
        // The rollback's authorization seam rides the same optional access as
        // the template twin: a cookie principal decides through the web UI's
        // IChangeSetAccess implementation instead of the token evaluator.
        private PolicyWriteFailure AuthorizeGlobalWrite(ClaimsPrincipal user,
            ChangeSets.IChangeSetAccess access = null)
        {
            if (access is not null)
                return access.CanWrite(ApiTokenResource.GlobalScope)
                    ? null
                    : new PolicyWriteFailure
                    {
                        Outcome = PolicyWriteOutcome.Forbidden,
                        Message = PolicyWriteResult.WriteDeniedMessage,
                    };

            return _authorization.AuthorizeWrite(user, ApiTokenResource.GlobalScope) switch
            {
                ApiTokenAuthorization.Allowed => null,
                ApiTokenAuthorization.Forbidden => new PolicyWriteFailure
                {
                    Outcome = PolicyWriteOutcome.Forbidden,
                    Message = PolicyWriteResult.WriteDeniedMessage,
                },
                _ => new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound },
            };
        }


        // === Undo-journal hooks (#1570) ===

        private InitiatorInfo InitiatorOf(ClaimsPrincipal user, ChangeSetWriteOptions write = null) =>
            ChangeSetRecorder.InitiatorOf(user, _users, write);

        private WritePreviewDto SchedulePreview(ChangeSetWriteOptions write, Guid scheduleId, string name)
        {
            var (sensors, products) = ScheduleReferencingNodes(scheduleId);
            var templates = ScheduleReferencingTemplates(scheduleId);

            var affected = sensors.Select(s => new PreviewAffectedEntityDto
                {
                    Id = s.Id.ToString(),
                    Path = s.FullPath,
                    Change = "detach schedule reference",
                })
                .Concat(products.Select(p => new PreviewAffectedEntityDto
                {
                    Id = p.Id.ToString(),
                    Path = p.FullPath,
                    Change = "detach schedule reference",
                }))
                .Concat(templates.Select(t => new PreviewAffectedEntityDto
                {
                    Id = t.Id.ToString(),
                    Path = t.Name,
                    Change = "detach schedule reference",
                }))
                .ToList();

            affected.Add(new PreviewAffectedEntityDto
            {
                Id = scheduleId.ToString(),
                Path = name,
                Change = "delete schedule",
            });

            return WritePreviews.Create(WritePreviews.CamelOf(ChangeSetEntryKind.AlertSchedule),
                WritePreviews.CamelOf(ChangeSetOperation.Delete), scheduleId, name, affected);
        }

        // The nodes the schedule-delete detach walks: sensors whose policies
        // reference the schedule (the cache's own lookup) and products with a
        // TTL reference (nested products included — the detach's product arm
        // enumerates them all).
        private (List<BaseSensorModel> Sensors, List<ProductModel> Products) ScheduleReferencingNodes(Guid scheduleId)
        {
            var sensors = _cache.GetSensorsByAlertSchedule(scheduleId) ?? [];
            var products = new List<ProductModel>();

            foreach (var root in _cache.GetProducts() ?? [])
                foreach (var product in EnumerateTree(root))
                    if (product.Policies.TTLPolicies.Any(t => t.ScheduleId == scheduleId))
                        products.Add(product);

            return (sensors, products);
        }

        private static IEnumerable<ProductModel> EnumerateTree(ProductModel product)
        {
            yield return product;

            foreach (var child in product.SubProducts.Values)
                foreach (var nested in EnumerateTree(child))
                    yield return nested;
        }

        private List<AlertTemplateModel> ScheduleReferencingTemplates(Guid scheduleId) =>
            (_cache.GetAlertTemplateModels() ?? [])
                .Where(t => (t.Policies ?? []).Any(p => p.ScheduleId == scheduleId) ||
                            (t.TtlEntries ?? []).Any(e => e.Policy?.ScheduleId == scheduleId))
                .ToList();

        private static Dictionary<Guid, (List<PolicyEntity> Data, List<PolicyEntity> Ttl)> CaptureSensorImages(
            IEnumerable<BaseSensorModel> sensors) =>
            sensors.ToDictionary(s => s.Id,
                s => (ChangeSetRecorder.SnapshotDataPolicies(s), ChangeSetRecorder.SnapshotSensorTtlPolicies(s)));

        private static NodePolicyImage SensorImage(BaseSensorModel sensor,
            (List<PolicyEntity> Data, List<PolicyEntity> Ttl) before,
            List<PolicyEntity> afterData, List<PolicyEntity> afterTtl) =>
            new()
            {
                NodeKind = ChangeSetNodeKind.Sensor,
                NodeId = sensor.Id,
                RootProductId = ChangeSetRecorder.RootProductIdOf(sensor),
                Path = sensor.FullPath,
                BeforePolicies = before.Data,
                AfterPolicies = afterData,
                BeforeTtlPolicies = before.Ttl,
                AfterTtlPolicies = afterTtl,
            };

        private static NodePolicyImage ProductImage(ProductModel product, List<PolicyEntity> beforeTtl) =>
            new()
            {
                NodeKind = ChangeSetNodeKind.Product,
                NodeId = product.Id,
                RootProductId = ChangeSetRecorder.RootProductIdOf(product),
                Path = product.FullPath,
                BeforeTtlPolicies = beforeTtl,
                AfterTtlPolicies = ChangeSetRecorder.SnapshotProductTtlPolicies(product),
            };
    }
}
