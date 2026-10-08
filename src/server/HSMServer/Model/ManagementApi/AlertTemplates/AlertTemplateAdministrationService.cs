using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using CoreTimeInterval = HSMServer.Core.Model.TimeInterval;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Core.Schedule;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.DataAlertTemplates;
using HSMServer.Model.ManagementApi.Alerts;
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
        private readonly ILogger<AlertTemplateAdministrationService> _logger;

        public AlertTemplateAdministrationService(ITreeValuesCache cache, IFolderManager folders,
            IChatsManager chats, IAlertScheduleProvider schedules, IApiTokenAuthorizationService authorization,
            ILogger<AlertTemplateAdministrationService> logger)
        {
            _cache = cache;
            _folders = folders;
            _chats = chats;
            _schedules = schedules;
            _authorization = authorization;
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
            AlertTemplateDto dto)
        {
            // An all-zero folder id references no folder, so a 400 leaks
            // nothing — and it keeps the folderId structural check reachable
            // instead of shadowed by the evaluator's 404 for Guid.Empty.
            if (dto.FolderId == Guid.Empty)
                return ValidationFailure("folderId", "The folder id is required.");

            // The authorization target is the requested folder; no validation
            // error is reported before this decision (404-first).
            if (AuthorizeFolder(write: true, dto.FolderId, user) is { } denied)
                return PolicyWriteResult<AlertTemplateDto>.Fail(denied.Outcome, message: denied.Message);

            if (!TryBuildValidatedTemplate(dto, id: Guid.NewGuid(), isCreate: true, user, out var model, out var errors))
                return PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Validation, errors);

            // Deliberately NOT tied to a cancellation token (see DeleteTemplateAsync
            // for the worst case): the cache persists before reconciling, so a
            // client-triggered cancellation mid-reconcile would leave
            // half-applied state. The web UI passes no token either — the
            // write completes once accepted.
            var (success, error) = await _cache.AddAlertTemplateAsync(model);

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
                return _cache.GetAlertTemplate(model.Id) is null
                    ? PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Conflict, message: error)
                    : PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Conflict, message: error,
                        details: new Dictionary<string, string> { ["templateId"] = model.Id.ToString() });
            }

            return PolicyWriteResult<AlertTemplateDto>.Ok(AlertTemplateDtoMapper.ToDto(model));
        }


        /// <summary>
        /// Update a template (upsert semantics). Moving it to another folder
        /// requires the owner's write access on BOTH folders; the result
        /// echoes the STORED template.
        /// </summary>
        public async Task<PolicyWriteResult<AlertTemplateDto>> UpdateTemplateAsync(Guid id, ClaimsPrincipal user,
            AlertTemplateDto dto)
        {
            var existing = _cache.GetAlertTemplate(id);

            if (existing is null)
                return PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.NotFound);

            // A template write is an indirect policy write on every matching
            // sensor of the target folder: moving a template needs write on
            // BOTH folders — the current one (the move destroys the old
            // folder's per-sensor policies) and the new one (the template
            // injects policies into its sensors).
            if (AuthorizeFolder(write: true, existing.FolderId, user) is { } deniedCurrent)
                return PolicyWriteResult<AlertTemplateDto>.Fail(deniedCurrent.Outcome, message: deniedCurrent.Message);

            // AFTER authorization (a body-shape 400 must not reveal that the
            // template exists to a caller outside its reach): an absent
            // folderId is a 400, not a 404 — Guid.Empty would otherwise fail
            // boundary resolution in the move-check below and shadow the
            // structural check.
            if (dto.FolderId == Guid.Empty)
                return ValidationFailure("folderId", "The folder id is required.");

            if (dto.FolderId != existing.FolderId &&
                AuthorizeFolder(write: true, dto.FolderId, user) is { } deniedTarget)
                return PolicyWriteResult<AlertTemplateDto>.Fail(deniedTarget.Outcome, message: deniedTarget.Message);

            if (!TryBuildValidatedTemplate(dto, id, isCreate: false, user, out var model, out var errors))
                return PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Validation, errors);

            var (success, error) = await _cache.AddAlertTemplateAsync(model);

            if (!success)
                return PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Conflict, message: error);

            // Echo the STORED template, not the request: the write normalizes
            // ids and chat names, and the caller must see the canonical
            // result.
            var stored = _cache.GetAlertTemplate(id) ?? model;

            return PolicyWriteResult<AlertTemplateDto>.Ok(AlertTemplateDtoMapper.ToDto(stored));
        }


        /// <summary>Delete a template and strip its per-sensor policies.</summary>
        public async Task<PolicyWriteResult> DeleteTemplateAsync(Guid id, ClaimsPrincipal user)
        {
            var template = _cache.GetAlertTemplate(id);

            if (template is null)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            if (AuthorizeFolder(write: true, template.FolderId, user) is { } denied)
                return PolicyWriteResult.Fail(denied.Outcome, message: denied.Message);

            // Deliberately NOT tied to a cancellation token:
            // RemoveAlertTemplateAsync strips the template-derived policies
            // from every matching sensor BEFORE it removes the template
            // itself, so a client-triggered cancellation mid-loop would leave
            // the template alive while some sensors have already lost their
            // alert policies — silent alert loss with nobody left to report
            // it to (the client is gone by definition). The delete completes
            // once accepted.
            var (success, error) = await _cache.RemoveAlertTemplateAsync(id);

            return success
                ? PolicyWriteResult.Ok()
                : PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: error);
        }


        // Maps the evaluator's decision onto the documented outcome codes:
        // null when allowed, Forbidden when the folder is in sight but the
        // caller cannot write there (a read-only token or the owner's Viewer
        // role), NotFound when the folder is invisible (indistinguishable
        // from an unknown id — the area's anti-enumeration rule).
        private PolicyWriteFailure AuthorizeFolder(bool write, Guid folderId, ClaimsPrincipal user) =>
            _authorization.AuthorizeWrite(user, FolderResource(folderId)) switch
            {
                ApiTokenAuthorization.Allowed => null,
                ApiTokenAuthorization.Forbidden => new PolicyWriteFailure
                {
                    Outcome = PolicyWriteOutcome.Forbidden,
                    Message = FolderWriteDeniedMessage,
                },
                _ => new PolicyWriteFailure { Outcome = PolicyWriteOutcome.NotFound },
            };

        private static PolicyWriteResult<AlertTemplateDto> ValidationFailure(string key, string message) =>
            PolicyWriteResult<AlertTemplateDto>.Fail(PolicyWriteOutcome.Validation,
                new Dictionary<string, string[]> { [key] = [message] });

        private static ApiTokenResource FolderResource(Guid folderId) =>
            new(ApiTokenResourceKind.Folder, folderId);


        // Structural validation, then entity reconstruction (inside a try — the
        // domain throws on legal-looking but unsupported input), then the
        // semantic checks ported from the web UI controller. Any accumulated
        // error fails the request.
        private bool TryBuildValidatedTemplate(AlertTemplateDto dto, Guid id, bool isCreate, ClaimsPrincipal user,
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
