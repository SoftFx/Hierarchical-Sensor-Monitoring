using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Authentication;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;
using HSMServer.Core.Cache;
using HSMServer.Core.ChangeSets;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Core.TableOfChanges;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.DataAlertTemplates;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Notifications.Chats;
using Microsoft.Extensions.Logging;

namespace HSMServer.Model.ManagementApi.AlertTemplates
{
    /// <summary>
    /// The write engine of the alert-template CRUD — the templates' twin of
    /// <see cref="PolicyAdministrationService"/> (#1500 pattern):
    /// transport-agnostic, returning <see cref="PolicyWriteResult{T}"/> so
    /// the REST controller and the MCP write tools render the same decision.
    /// </summary>
    // Extracted VERBATIM from AlertTemplatesApiController (#1393 precedent):
    // the write paths (authorization ordering, structural/semantic
    // validation, cache calls, the create-409 templateId disclosure) existed
    // only inside the controller, so an MCP write tool could not delegate to
    // them. The extraction is behavior-preserving — the controller's suite is
    // the regression net and must pass unchanged; every comment that
    // documented a rule inline moved with its code.
    //
    // Wire contract notes (unchanged):
    //  - structural validation answers 400 (a pre-#1500 surface whose shipped
    //    contract is Validation, not the #1500 Invalid/422) — the shared
    //    envelope's Validation outcome;
    //  - a template write is an indirect policy write on every matching
    //    sensor of the target folder: moving a template needs write on BOTH
    //    folders (the move destroys the old folder's per-sensor policies);
    //  - writes are never client-cancellable (the cache persists before
    //    reconciling; see CreateTemplateAsync).
    public sealed class AlertTemplateAdministrationService
    {
        // Size guard rails the web UI gets from its widgets; the API states
        // them explicitly. They bound the collection COUNTS and the name
        // length only — individual strings (paths, message templates, target
        // values) stay bounded by the request body limit alone.
        public const int MaxNameLength = 200;
        public const int MaxPaths = 100;
        public const int MaxPolicies = 100;

        private const string FolderWriteDeniedMessage =
            "The token is read-only or the token's owner cannot write at this folder.";

        private readonly ITreeValuesCache _cache;
        private readonly IFolderManager _folders;
        private readonly IChatsManager _chats;
        private readonly IAlertScheduleProvider _schedules;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IUserManager _users;
        private readonly ChangeSetRecorder _changeSets;
        private readonly ILogger<AlertTemplateAdministrationService> _logger;

        public AlertTemplateAdministrationService(ITreeValuesCache cache, IFolderManager folders,
            IChatsManager chats, IAlertScheduleProvider schedules, IApiTokenAuthorizationService authorization,
            IUserManager users, ChangeSetRecorder changeSets, ILogger<AlertTemplateAdministrationService> logger)
        {
            _cache = cache;
            _folders = folders;
            _chats = chats;
            _schedules = schedules;
            _authorization = authorization;
            _users = users;
            _changeSets = changeSets;
            _logger = logger;
        }


        /// <summary>
        /// Create a template. A client-sent id is ignored (the server generates
        /// one — the cache Add is an upsert by id, so honoring a client-chosen
        /// id would let a folder-scoped token overwrite a template in a folder
        /// it cannot even see); the result echoes the STORED template —
        /// normalized ids, chat display names.
        /// </summary>
        public async Task<PolicyWriteResult<AlertTemplateDto>> CreateTemplateAsync(ClaimsPrincipal user,
            AlertTemplateDto dto, ChangeSetWriteOptions write = null, ChangeSets.IChangeSetAccess access = null)
        {
            // A JSON-null body reaches here only from MCP (REST's
            // [ApiController] binding rejects it with a 400 before the
            // service runs). It follows the folderId structural-check
            // precedent below — a null references no folder, so the 400
            // leaks nothing.
            if (dto is null)
                return ValidationFailure("template", "The template body is required.");

            // An all-zero folder id references no folder, so a 400 leaks
            // nothing — and it keeps the folderId structural check reachable
            // instead of shadowed by the evaluator's 404 for Guid.Empty.
            if (dto.FolderId == Guid.Empty)
                return ValidationFailure("folderId", "The folder id is required.");

            // The authorization target is the requested folder; no validation
            // error is reported before this decision (404-first).
            if (AuthorizeFolder(dto.FolderId, user, access) is { } denied)
                return PolicyWriteResult<AlertTemplateDto>.Fail(denied.Outcome, message: denied.Message);

            if (!TryBuildValidatedTemplate(dto, id: Guid.NewGuid(), isCreate: true, out var model, out var errors))
                return PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Validation, errors);

            return await CreateValidatedTemplateAsync(user, model, write);
        }

        /// <summary>
        /// The web editor's create entry (#1588: the single write path): the
        /// form's already-validated <see cref="AlertTemplateModel"/> rides
        /// the same authorization, gates and undo journaling as the DTO path.
        /// </summary>
        public Task<PolicyWriteResult<AlertTemplateDto>> CreateTemplateAsync(ClaimsPrincipal user,
            AlertTemplateModel model, ChangeSetWriteOptions write = null, ChangeSets.IChangeSetAccess access = null)
        {
            if (AuthorizeFolder(model.FolderId, user, access) is { } denied)
                return Task.FromResult(
                    PolicyWriteResult<AlertTemplateDto>.Fail(denied.Outcome, message: denied.Message));

            return CreateValidatedTemplateAsync(user, model, write);
        }

        private async Task<PolicyWriteResult<AlertTemplateDto>> CreateValidatedTemplateAsync(
            ClaimsPrincipal user, AlertTemplateModel model, ChangeSetWriteOptions write)
        {
            // #1570 dry-run: the template body is valid; report the fan-out
            // target set (matched sensors; a fresh id has no stale holders)
            // and apply nothing.
            if (write is { DryRun: true })
                return TemplatePreview(write, ChangeSetOperation.Create, model, previousFolderId: null);

            var changeSetId = ChangeSetRecorder.ResolveChangeSetId(write);

            // #1570 before-images: the touched sensor set (matched ∪ stale)
            // with both policy lists, snapshotted BEFORE the apply. The
            // rollback restores these exact lists after compensating the
            // template row itself.
            var targets = TemplateTargets(model, previousFolderId: null, scanPaths: true);
            var beforeImages = CaptureBeforeImages(targets);

            // Deliberately NOT tied to a cancellation token (see DeleteTemplateAsync
            // for the worst case): the cache persists before reconciling, so a
            // client-triggered cancellation mid-reconcile would leave
            // half-applied state. The web UI passes no token either — the
            // write completes once accepted.
            var (success, error, warnings) = await _cache.AddAlertTemplateAsync(model);

            if (!success)
            {
                // The apply failure does NOT roll back the template (retry
                // semantics), so a create-path 409 can leave a live resource
                // the caller was never told the id of — and a plain re-POST
                // would trip the name-uniqueness 400. Disclose the created id
                // so the caller can fix the template up via PUT. The
                // pre-persist "no products" conflict leaves nothing behind:
                // the cache lookup returns null there (the id is freshly
                // generated, so no false positive).
                var partiallyApplied = _cache.GetAlertTemplate(model.Id);

                if (partiallyApplied is not null)
                    // #1570: the row is saved and some matched sensors may
                    // already carry the fan-out — journal the PARTIAL apply
                    // (the captured before-images, the live after-state) so
                    // the half-applied write keeps its undo point instead of
                    // leaving changed sensors no change set can explain.
                    RecordTemplateEntry(changeSetId, InitiatorOf(user, write), ChangeSetOperation.Create,
                        model.Id, model.FolderId, model.Name, beforeTemplate: null,
                        afterTemplate: partiallyApplied.ToEntity(), targets, beforeImages);

                return partiallyApplied is null
                    ? PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Conflict, message: error)
                    : PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Conflict, message: error,
                        details: new Dictionary<string, string> { ["templateId"] = model.Id.ToString() });
            }

            var stored = _cache.GetAlertTemplate(model.Id) ?? model;

            // #1401: a persisting save with per-policy rejections is a
            // SUCCESS — the warnings name the sensors whose alerts were not
            // created and ride the echoed DTO (rendered by REST and MCP with
            // the same strings).
            var created = PolicyWriteResult<AlertTemplateDto>.Ok(
                AlertTemplateDtoMapper.ToDto(stored, warnings));

            RecordTemplateEntry(changeSetId, InitiatorOf(user, write), ChangeSetOperation.Create,
                model.Id, model.FolderId, model.Name, beforeTemplate: null, afterTemplate: stored.ToEntity(),
                targets, beforeImages);

            return created;
        }


        /// <summary>
        /// Update a template (upsert semantics). Moving it to another folder
        /// requires the owner's write access on BOTH folders; the result
        /// echoes the STORED template.
        /// </summary>
        public async Task<PolicyWriteResult<AlertTemplateDto>> UpdateTemplateAsync(Guid id, ClaimsPrincipal user,
            AlertTemplateDto dto, ChangeSetWriteOptions write = null, ChangeSets.IChangeSetAccess access = null)
        {
            var existing = _cache.GetAlertTemplate(id);

            if (existing is null)
                return PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.NotFound);

            // A template write is an indirect policy write on every matching
            // sensor of the target folder: moving a template needs write on
            // BOTH folders — the current one (the move destroys the old
            // folder's per-sensor policies) and the new one (the template
            // injects policies into its sensors).
            if (AuthorizeFolder(existing.FolderId, user, access) is { } deniedCurrent)
                return PolicyWriteResult<AlertTemplateDto>.Fail(deniedCurrent.Outcome, message: deniedCurrent.Message);

            // AFTER authorization (a body-shape 400 must not reveal that the
            // template exists to a caller outside its reach): an absent
            // folderId is a 400, not a 404 — Guid.Empty would otherwise fail
            // boundary resolution in the move-check below and shadow the
            // structural check. A JSON-null body (MCP-only; REST binding
            // rejects it first) answers the same field-keyed shape.
            if (dto is null)
                return ValidationFailure("template", "The template body is required.");

            if (dto.FolderId == Guid.Empty)
                return ValidationFailure("folderId", "The folder id is required.");

            if (dto.FolderId != existing.FolderId &&
                AuthorizeFolder(dto.FolderId, user, access) is { } deniedTarget)
                return PolicyWriteResult<AlertTemplateDto>.Fail(deniedTarget.Outcome, message: deniedTarget.Message);

            if (!TryBuildValidatedTemplate(dto, id, isCreate: false, out var model, out var errors))
                return PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Validation, errors);

            return await UpdateValidatedTemplateAsync(id, user, model, existing, write);
        }

        /// <summary>
        /// The web editor's update entry (#1588: the single write path): the
        /// form's already-validated model, same authorization (both folders on
        /// a move), gates and undo journaling as the DTO path.
        /// </summary>
        public async Task<PolicyWriteResult<AlertTemplateDto>> UpdateTemplateAsync(Guid id, ClaimsPrincipal user,
            AlertTemplateModel model, ChangeSetWriteOptions write = null, ChangeSets.IChangeSetAccess access = null)
        {
            var existing = _cache.GetAlertTemplate(id);

            if (existing is null)
                return PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.NotFound);

            if (AuthorizeFolder(existing.FolderId, user, access) is { } deniedCurrent)
                return PolicyWriteResult<AlertTemplateDto>.Fail(deniedCurrent.Outcome, message: deniedCurrent.Message);

            if (model.FolderId != existing.FolderId &&
                AuthorizeFolder(model.FolderId, user, access) is { } deniedTarget)
                return PolicyWriteResult<AlertTemplateDto>.Fail(deniedTarget.Outcome, message: deniedTarget.Message);

            return await UpdateValidatedTemplateAsync(id, user, model, existing, write);
        }

        private async Task<PolicyWriteResult<AlertTemplateDto>> UpdateValidatedTemplateAsync(Guid id,
            ClaimsPrincipal user, AlertTemplateModel model, AlertTemplateModel existing, ChangeSetWriteOptions write)
        {
            // #1570 dry-run: report the fan-out target set of the NEW body —
            // matched sensors plus stale holders of the id (sensors the edit
            // would prune, including the old folder's on a move).
            if (write is { DryRun: true })
                return TemplatePreview(write, ChangeSetOperation.Update, model, existing.FolderId);

            var changeSetId = ChangeSetRecorder.ResolveChangeSetId(write);
            var targets = TemplateTargets(model, existing.FolderId, scanPaths: true);
            var beforeImages = CaptureBeforeImages(targets);
            var beforeTemplate = existing.ToEntity();

            var (success, error, warnings) = await _cache.AddAlertTemplateAsync(model);

            if (!success)
            {
                // #1570: the upsert persists the row BEFORE the fan-out, so
                // an apply conflict can leave some sensors already updated —
                // journal the partial apply (the create path's rule) with the
                // live row and lists as the after-image.
                RecordTemplateEntry(changeSetId, InitiatorOf(user, write), ChangeSetOperation.Update,
                    id, existing.FolderId, existing.Name, beforeTemplate,
                    _cache.GetAlertTemplate(id)?.ToEntity(), targets, beforeImages);

                return PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Conflict, message: error);
            }

            // Echo the STORED template, not the request: the write normalizes
            // ids and chat names, and the caller must see the canonical
            // result. The #1401 warnings ride the echo (see CreateTemplateAsync).
            var stored = _cache.GetAlertTemplate(id) ?? model;

            var updated = PolicyWriteResult<AlertTemplateDto>.Ok(AlertTemplateDtoMapper.ToDto(stored, warnings));

            RecordTemplateEntry(changeSetId, InitiatorOf(user, write), ChangeSetOperation.Update,
                id, existing.FolderId, existing.Name, beforeTemplate, stored.ToEntity(), targets, beforeImages);

            return updated;
        }


        /// <summary>Delete a template and strip its per-sensor policies.</summary>
        public async Task<PolicyWriteResult> DeleteTemplateAsync(Guid id, ClaimsPrincipal user,
            ChangeSetWriteOptions write = null, ChangeSets.IChangeSetAccess access = null)
        {
            var template = _cache.GetAlertTemplate(id);

            if (template is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            if (AuthorizeFolder(template.FolderId, user, access) is { } denied)
                return PolicyWriteResult.Fail(denied.Outcome, message: denied.Message);

            // #1570 dry-run: report the sensors the strip would touch (the
            // holders of this template's policies).
            if (write is { DryRun: true })
                return PolicyWriteResult.OkPreview(TemplatePreview(write, ChangeSetOperation.Delete,
                    template, template.FolderId).Preview);

            var changeSetId = ChangeSetRecorder.ResolveChangeSetId(write);

            // The touched set of a delete: sensors holding this template's
            // policies (no matched set — the body is going away).
            var targets = TemplateTargets(template, template.FolderId, scanPaths: false);
            var beforeImages = CaptureBeforeImages(targets);
            var beforeTemplate = template.ToEntity();

            // Deliberately NOT tied to a cancellation token:
            // RemoveAlertTemplateAsync strips the template-derived policies
            // from every matching sensor BEFORE it removes the template
            // itself, so a client-triggered cancellation mid-loop would leave
            // the template alive while some sensors have already lost their
            // alert policies — silent alert loss with nobody left to report
            // it to (the client is gone by definition). The delete completes
            // once accepted.
            var removed = await RemoveTemplateCoreAsync(id, user, access);

            if (!removed.Success)
            {
                // #1570: a strip conflict (the cache strips per-sensor
                // policies BEFORE deleting the row) leaves some sensors
                // already stripped with the row alive — journal the partial
                // strip so it keeps its undo point; auth/NotFound failures
                // changed nothing and journal nothing.
                if (removed.Failure.Outcome == PolicyWriteOutcome.Conflict)
                    RecordTemplateEntry(changeSetId, InitiatorOf(user, write), ChangeSetOperation.Delete,
                        id, template.FolderId, template.Name, beforeTemplate,
                        _cache.GetAlertTemplate(id)?.ToEntity(), targets, beforeImages);

                return removed;
            }

            RecordTemplateEntry(changeSetId, InitiatorOf(user, write), ChangeSetOperation.Delete,
                id, template.FolderId, template.Name, beforeTemplate, afterTemplate: null, targets, beforeImages);

            return PolicyWriteResult.Ok();
        }


        // === Rollback restore paths (#1570) ===

        // Non-journaling delete core: authorization + the cache strip, no
        // capture and no journal entry. The rollback engine calls this when
        // compensating a template CREATE — it journals the whole rollback as
        // its own change set.
        internal async Task<PolicyWriteResult> RemoveTemplateCoreAsync(Guid id, ClaimsPrincipal user,
            ChangeSets.IChangeSetAccess access = null)
        {
            var template = _cache.GetAlertTemplate(id);

            if (template is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            if (AuthorizeFolder(template.FolderId, user, access) is { } denied)
                return PolicyWriteResult.Fail(denied.Outcome, message: denied.Message);

            var (success, error) = await _cache.RemoveAlertTemplateAsync(id);

            return success
                ? PolicyWriteResult.Ok()
                : PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: error);
        }

        // Restore a recorded template row VERBATIM (same id — unlike the
        // public create, which generates one) and re-run the fan-out.
        // Validation is skipped by design: the recorded body was valid when
        // it was written, and state it referenced (chats, schedules) may have
        // moved on since — the rollback's contract is to restore the recorded
        // configuration, not to re-judge it against today's referential state.
        internal async Task<PolicyWriteResult> RestoreTemplateEntityAsync(AlertTemplateEntity entity,
            ClaimsPrincipal user, ChangeSets.IChangeSetAccess access = null)
        {
            if (AuthorizeFolder(entity.FolderId, user, access) is { } denied)
                return PolicyWriteResult.Fail(denied.Outcome, message: denied.Message);

            AlertTemplateModel model;

            try
            {
                model = new AlertTemplateModel(entity);
            }
            catch (Exception e)
            {
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict,
                    message: $"The recorded template image could not be reconstructed: {e.Message}");
            }

            var (success, error, _) = await _cache.AddAlertTemplateAsync(model);

            return success
                ? PolicyWriteResult.Ok()
                : PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: error);
        }


        // Maps the evaluator's decision onto the documented outcome codes:
        // null when allowed, Forbidden when the folder is in sight but the
        // caller cannot write there (a read-only token or the owner's Viewer
        // role), NotFound when the folder is invisible (indistinguishable
        // from an unknown id — the area's anti-enumeration rule). Every write
        // path here IS a write (the read half stayed in AlertReadService), so
        // the decision is always AuthorizeWrite.
        //
        // The rollback's authorization seam: a web-UI rollback supplies the
        // cookie twin instead — the evaluator resolves HsmApiToken principals
        // only and would answer NotFound for a cookie user on every restore.
        private PolicyWriteFailure AuthorizeFolder(Guid folderId, ClaimsPrincipal user,
            ChangeSets.IChangeSetAccess access = null)
        {
            if (access is not null)
                return access.CanWrite(FolderResource(folderId))
                    ? null
                    : new PolicyWriteFailure
                    {
                        Outcome = PolicyWriteOutcome.Forbidden,
                        Message = FolderWriteDeniedMessage,
                    };

            return _authorization.AuthorizeWrite(user, FolderResource(folderId)) switch
            {
                ApiTokenAuthorization.Allowed => null,
                ApiTokenAuthorization.Forbidden => new PolicyWriteFailure
                {
                    Outcome = PolicyWriteOutcome.Forbidden,
                    Message = FolderWriteDeniedMessage,
                },
                _ => new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound },
            };
        }

        private static PolicyWriteResult<AlertTemplateDto> ValidationFailure(string key, string message) =>
            PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Validation,
                new Dictionary<string, string[]> { [key] = [message] });

        private static ApiTokenResource FolderResource(Guid folderId) =>
            new(ApiTokenResourceKind.Folder, folderId);


        // === Undo-journal hooks (#1570) ===

        // The write initiator resolves through the shared ChangeSetRecorder
        // helper (the journal's fork rule compares the author strings).
        private InitiatorInfo InitiatorOf(ClaimsPrincipal user, ChangeSetWriteOptions write = null) =>
            ChangeSetRecorder.InitiatorOf(user, _users, write);

        // Dry-run preview of a template write: the fan-out target set (matched
        // by the new paths, plus stale holders the edit would prune — the old
        // folder's holders included on a move), nothing applied.
        private PolicyWriteResult<AlertTemplateDto> TemplatePreview(ChangeSetWriteOptions write,
            ChangeSetOperation operation, AlertTemplateModel template, Guid? previousFolderId)
        {
            var targets = TemplateTargets(template, previousFolderId, scanPaths: operation != ChangeSetOperation.Delete);

            return PolicyWriteResult<AlertTemplateDto>.OkPreview(WritePreviews.Create(
                WritePreviews.CamelOf(ChangeSetEntryKind.AlertTemplate), WritePreviews.CamelOf(operation),
                template.Id, template.Name,
                targets.Select(sensor => new PreviewAffectedEntityDto
                {
                    Id = sensor.Id.ToString(),
                    Path = sensor.FullPath,
                    Change = "fan-out",
                })));
        }

        // The sensors a template write touches, mirroring the cache's own
        // enumeration in AddAlertTemplateAsync (matched by wildcard over the
        // folder's products, plus stale holders of the template id — sensors
        // of the old folder included when the template moves). Deleting scans
        // holders only: the body is going away, so path matching adds nothing
        // the strip would touch.
        private List<BaseSensorModel> TemplateTargets(AlertTemplateModel template, Guid? previousFolderId,
            bool scanPaths)
        {
            var targets = new HashSet<BaseSensorModel>();

            if (scanPaths)
                foreach (var path in template.Paths.Where(p => !string.IsNullOrEmpty(p)))
                    foreach (var sensor in _cache.GetSensors(path, template.GetSensorType(), template.FolderId) ?? [])
                        targets.Add(sensor);

            foreach (var product in (_cache.GetProducts() ?? []).Where(p =>
                         p.FolderId == template.FolderId ||
                         (previousFolderId is { } previous && p.FolderId == previous)))
                foreach (var sensor in product.GetAllSensors())
                {
                    if (targets.Contains(sensor))
                        continue;

                    if (sensor.Policies.TTLPolicies.Any(t => t.TemplateId == template.Id) ||
                        sensor.Policies.Any(p => p.TemplateId == template.Id))
                        targets.Add(sensor);
                }

            return [.. targets];
        }

        private static Dictionary<Guid, (List<PolicyEntity> Data, List<PolicyEntity> Ttl)> CaptureBeforeImages(
            IEnumerable<BaseSensorModel> sensors) =>
            sensors.ToDictionary(s => s.Id,
                s => (ChangeSetRecorder.SnapshotDataPolicies(s), ChangeSetRecorder.SnapshotSensorTtlPolicies(s)));

        // One journal entry per template write: the template row's
        // before/after image plus a per-sensor full-list image of the fan-out
        // (#1570 story 19 — rollback restores both the template and every
        // affected sensor's policy list). Returns the id the entry landed in
        // (null when the journal write failed) — the callers stamp the echoed
        // change-set id ONLY on a landed id, so a client never receives an
        // undo handle that does not exist.
        private Guid? RecordTemplateEntry(Guid changeSetId, InitiatorInfo initiator, ChangeSetOperation operation,
            Guid templateId, Guid folderId, string templateName, AlertTemplateEntity beforeTemplate,
            AlertTemplateEntity afterTemplate, List<BaseSensorModel> targets,
            Dictionary<Guid, (List<PolicyEntity> Data, List<PolicyEntity> Ttl)> beforeImages)
        {
            return _changeSets.Record(changeSetId, initiator, new ChangeSetEntry
            {
                Kind = ChangeSetEntryKind.AlertTemplate,
                Operation = operation,
                EntityId = templateId,
                ParentId = folderId,
                Path = templateName,
                BeforeTemplate = beforeTemplate,
                AfterTemplate = afterTemplate,
                Fanout = targets.Select(sensor => new NodePolicyImage
                {
                    NodeKind = ChangeSetNodeKind.Sensor,
                    NodeId = sensor.Id,
                    RootProductId = ChangeSetRecorder.RootProductIdOf(sensor),
                    Path = sensor.FullPath,
                    BeforePolicies = beforeImages[sensor.Id].Data,
                    AfterPolicies = ChangeSetRecorder.SnapshotDataPolicies(sensor),
                    BeforeTtlPolicies = beforeImages[sensor.Id].Ttl,
                    AfterTtlPolicies = ChangeSetRecorder.SnapshotSensorTtlPolicies(sensor),
                }).ToList(),
            });
        }


        // Structural validation, then entity reconstruction (inside a try — the
        // domain throws on legal-looking but unsupported input), then the
        // semantic checks ported from the web UI controller. Any accumulated
        // error fails the request.
        private bool TryBuildValidatedTemplate(AlertTemplateDto dto, Guid id, bool isCreate,
            out AlertTemplateModel model, out Dictionary<string, string[]> errors)
        {
            var errorMap = new Dictionary<string, List<string>>();

            AddStructuralErrors(dto, id, isCreate, errorMap);

            model = null;

            if (errorMap.Count == 0)
            {
                // One pass over the chat manager serves both the mapper
                // (canonical display names) and the semantic chat-availability
                // rule; built only when something will actually be
                // reconstructed.
                var chatsById = ChatIndex();

                try
                {
                    model = new AlertTemplateModel(AlertTemplateDtoMapper.ToEntity(dto, id, chatsById));
                }
                catch (Exception e)
                {
                    // e.g. Policy.Apply throws NotImplementedException for a
                    // condition property the sensor-type policy does not support,
                    // or FormatException for an unparseable Const target. A 400
                    // carrying the reconstruction reason — never a 500 out of
                    // the API area, and never one static string masking the
                    // actual cause (#1439).
                    _logger.LogWarning(e, "API alert-template payload could not be reconstructed");
                    Add(errorMap, "policies", $"The template could not be parsed: {e.Message}");
                }

                if (model is not null)
                    AddSemanticErrors(dto, model, id, errorMap, chatsById);
            }

            errors = errorMap.ToDictionary(p => p.Key, p => p.Value.ToArray());

            return errors.Count == 0;
        }

        private void AddStructuralErrors(AlertTemplateDto dto, Guid id, bool isCreate,
            Dictionary<string, List<string>> errors)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                Add(errors, "name", "The name is required.");
            else if (dto.Name.Length > MaxNameLength)
                Add(errors, "name", $"The name must be at most {MaxNameLength} characters.");

            if (dto.Paths is null || dto.Paths.All(string.IsNullOrWhiteSpace))
                Add(errors, "paths", "At least one path template is required.");
            else if (dto.Paths.Count > MaxPaths)
                Add(errors, "paths", $"At most {MaxPaths} path templates are allowed.");

            if ((dto.Policies?.Count ?? 0) + (dto.TtlPolicies?.Count ?? 0) > MaxPolicies)
                Add(errors, "policies", $"At most {MaxPolicies} policies (regular and TTL combined) are allowed.");

            if (dto.SensorType != AlertTemplateModel.AnyType && !Enum.IsDefined<SensorType>((SensorType)dto.SensorType))
                Add(errors, "sensorType", $"Unknown sensor type {dto.SensorType}.");

            if ((dto.TtlPolicies?.Count ?? 0) != (dto.Ttls?.Count ?? 0))
                Add(errors, "ttlPolicies", "ttlPolicies and ttls must be parallel lists of the same length.");

            if (!isCreate && dto.Id != Guid.Empty && dto.Id != id)
                Add(errors, "id", "The body id must match the route id.");

            // Null list elements are rejected HERE, before the domain
            // reconstruction: System.Text.Json materialises "policies":
            // [null] happily, and a null dereference inside the mapper would
            // otherwise escape as a 500 (the structural pass runs outside the
            // reconstruction try). Keys are item-indexed
            // (policies[2].conditions[0].operation) so a client can LOCATE
            // the offending entry — the shape [ApiController] itself produces
            // for binding failures.

            // Duplicate non-empty policy ids are rejected: at apply time the
            // id becomes the sensor policy's TemplateAlertId — the matching
            // key — so two policies sharing one id silently collapse into one
            // (GroupBy(First()) in the cache).
            var seenPolicyIds = new HashSet<Guid>();

            foreach (var (policy, index) in (dto.Policies ?? []).Select((p, i) => (p, i)))
            {
                if (policy is null)
                {
                    Add(errors, $"policies[{index}]", "A policy entry must not be null.");
                    continue;
                }

                if (policy.Id != Guid.Empty && !seenPolicyIds.Add(policy.Id))
                    Add(errors, $"policies[{index}].id", "Duplicate policy id — ids must be unique across policies and ttlPolicies.");

                AddPolicyStructureErrors(policy, $"policies[{index}]", errors);
            }

            foreach (var (policy, index) in (dto.TtlPolicies ?? []).Select((p, i) => (p, i)))
            {
                if (policy is null)
                {
                    Add(errors, $"ttlPolicies[{index}]", "A TTL policy entry must not be null.");
                    continue;
                }

                if (policy.Id != Guid.Empty && !seenPolicyIds.Add(policy.Id))
                    Add(errors, $"ttlPolicies[{index}].id", "Duplicate policy id — ids must be unique across policies and ttlPolicies.");

                AddPolicyStructureErrors(policy, $"ttlPolicies[{index}]", errors);
            }

            // The interval enum is SPARSE (FromFolder=-100 … Year): an
            // undefined value persists fine but TimeIntervalModel.GetShiftedTime
            // throws NotImplementedException for it — inside the timeout-scan
            // loop, outside this try. The web UI cannot produce it
            // (dropdown); the API must not accept it. When the ticks are
            // authoritative (Ticks/FromFolder), they must also keep now +
            // ticks inside the DateTime range — AddTicks throws outside it,
            // in the same loop.
            foreach (var (interval, index) in (dto.Ttls ?? []).Select((t, i) => (t, i)))
            {
                if (interval is null)
                {
                    Add(errors, $"ttls[{index}]", "An interval entry must not be null.");
                    continue;
                }

                if (!Enum.IsDefined<CoreTimeInterval>((CoreTimeInterval)interval.Interval))
                    Add(errors, $"ttls[{index}]", $"Unknown interval {interval.Interval}.");

                if ((CoreTimeInterval)interval.Interval is CoreTimeInterval.Ticks or CoreTimeInterval.FromFolder &&
                    (interval.Ticks <= 0 || interval.Ticks > DateTime.MaxValue.Ticks - DateTime.UtcNow.Ticks))
                    Add(errors, $"ttls[{index}]", "Interval ticks must be positive and keep the shifted time inside the DateTime range.");
            }
        }

        // Every enum byte is validated BEFORE the domain casts it: the entity
        // reconstruction casts without checks and unknown values either throw
        // or silently coerce (SensorStatus -> Error). Errors carry the item
        // path (e.g. "policies[2].conditions[0].operation") so a client can
        // locate them.
        private static void AddPolicyStructureErrors(AlertPolicyDto policy, string prefix,
            Dictionary<string, List<string>> errors)
        {
            foreach (var (condition, conditionIndex) in (policy.Conditions ?? []).Select((c, i) => (c, i)))
            {
                var conditionPrefix = $"{prefix}.conditions[{conditionIndex}]";

                if (condition?.Target is null)
                {
                    Add(errors, $"{conditionPrefix}.target", "Every condition must carry a target.");
                    continue;
                }

                if (!Enum.IsDefined<PolicyOperation>((PolicyOperation)condition.Operation))
                    Add(errors, $"{conditionPrefix}.operation", $"Unknown condition operation {condition.Operation}.");

                if (!Enum.IsDefined<PolicyProperty>((PolicyProperty)condition.Property))
                    Add(errors, $"{conditionPrefix}.property", $"Unknown condition property {condition.Property}.");

                if (!Enum.IsDefined<PolicyCombination>((PolicyCombination)condition.Combination))
                    Add(errors, $"{conditionPrefix}.combination", $"Unknown condition combination {condition.Combination}.");

                if (!Enum.IsDefined<TargetType>((TargetType)condition.Target.Type))
                    Add(errors, $"{conditionPrefix}.target.type", $"Unknown condition target type {condition.Target.Type}.");
            }

            if (!Enum.IsDefined<SensorStatus>((SensorStatus)policy.SensorStatus))
                Add(errors, $"{prefix}.sensorStatus", $"Unknown sensor status {policy.SensorStatus}.");

            if (!Enum.IsDefined<AlertRepeatMode>((AlertRepeatMode)(policy.Schedule?.RepeateMode ?? 0)))
                Add(errors, $"{prefix}.schedule", $"Unknown repeat mode {policy.Schedule?.RepeateMode}.");

            var timeTicks = policy.Schedule?.TimeTicks ?? 0;

            if (timeTicks < 0 || timeTicks > DateTime.MaxValue.Ticks)
                Add(errors, $"{prefix}.schedule", "schedule.timeTicks is outside the supported range.");

            if (policy.Destination?.Chats is { Count: > 0 } chats)
                foreach (var chatKey in chats.Keys)
                    if (!Guid.TryParse(chatKey, out _))
                        Add(errors, $"{prefix}.destination", $"The chat id '{chatKey}' is not a valid Guid.");
        }

        // Semantic checks ported from the cookie controller (same order, same
        // error strings) plus the chat-availability rule the web UI enforces
        // through its dropdown: a chat is offerable when it is global or
        // bound to the template's folder. Runs only after authorization and
        // structural validation passed.
        private void AddSemanticErrors(AlertTemplateDto dto, AlertTemplateModel model, Guid id,
            Dictionary<string, List<string>> errors, Dictionary<string, Chat> chatsById)
        {
            // Global and case-sensitive, exactly like the web UI.
            if (_cache.GetAlertTemplateModels()?.Any(x => x.Name == model.Name && x.Id != id) == true)
                Add(errors, "name", "The name must be unique.");

            if (!model.TryApplyPathTemplates(out var pathError))
                Add(errors, "paths", $"Invalid path template: {pathError}");

            foreach (var mismatchError in AlertTemplatePathValidation.GetPathTypeMismatchErrors(_cache, model))
                Add(errors, "paths", mismatchError);

            // A scheduleId the provider does not know is a dangling reference
            // the web UI cannot create (its dropdown offers existing
            // schedules only); at evaluation IsWorkingTime logs an error and
            // silently treats the policy as always-in-working-time.
            foreach (var scheduleId in (dto.Policies ?? []).Concat(dto.TtlPolicies ?? [])
                         .Where(p => p?.ScheduleId is not null)
                         .Select(p => p.ScheduleId.Value)
                         .Distinct())
                if (_schedules.GetSchedule(scheduleId) is null)
                    Add(errors, "scheduleId", $"Unknown schedule '{scheduleId}'.");

            AddChatAvailabilityErrors(dto, errors, chatsById);
        }

        // Destination chat ids of every policy (regular and TTL) must resolve
        // to a chat that is global (bound to no folder) or bound to the
        // template's folder — the same predicate the web UI's chat dropdown
        // applies. Runs on the DTO side: the mapper preserves chat keys 1:1,
        // and the semantic phase still owns the rule.
        private void AddChatAvailabilityErrors(AlertTemplateDto dto, Dictionary<string, List<string>> errors,
            Dictionary<string, Chat> chatsById)
        {
            var boundChats = _folders.TryGetValue(dto.FolderId, out var folder) && folder.TryGetChats(out var chats)
                ? chats
                : null;

            foreach (var policy in (dto.Policies ?? []).Concat(dto.TtlPolicies ?? []))
            {
                if (policy.Destination?.Chats is not { Count: > 0 } referencedChats)
                    continue;

                foreach (var chatKey in referencedChats.Keys)
                {
                    if (!chatsById.TryGetValue(chatKey, out var chat))
                    {
                        Add(errors, "destination", $"Unknown chat '{chatKey}'.");
                        continue;
                    }

                    if (chat.Folders.Count != 0 && (boundChats is null || !boundChats.Contains(chat.Id)))
                        Add(errors, "destination", $"Chat '{chatKey}' is not available in the template's folder.");
                }
            }
        }

        private Dictionary<string, Chat> ChatIndex()
        {
            var chatsById = new Dictionary<string, Chat>();

            foreach (var chat in _chats.GetValues() ?? [])
                chatsById[chat.Id.ToString()] = chat;

            return chatsById;
        }

        private static void Add(Dictionary<string, List<string>> errors, string key, string message) =>
            (errors.TryGetValue(key, out var list) ? list : errors[key] = []).Add(message);
    }
}
