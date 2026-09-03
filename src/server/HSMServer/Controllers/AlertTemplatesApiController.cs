using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HSMCommon.Model;
using HSMServer.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Policies;
using HSMServer.Extensions;
using HSMServer.Folders;
using HSMServer.Model.DataAlertTemplates;
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace HSMServer.Controllers
{
    // REST CRUD over alert templates — the first /api/v1 resource controller (#1351,
    // epic #1347). The class attributes are exactly what ManagementApiGuardMiddleware
    // admits into the management area: [ManagementApi] + the HsmApiToken management
    // policy (bearer token only, SitePort only, plain 401 challenge) — this controller
    // never runs under the cookie scheme and derives from ControllerBase, not the
    // cookie-world BaseController.
    //
    // Authorization is per request through IApiTokenAuthorizationService, at the
    // template's FOLDER boundary: reads need alerts:read, writes alerts:write (which
    // additionally requires the owner's Manager role at the boundary). The evaluator's
    // 403/404 split is preserved verbatim — an invisible or out-of-reach folder is a
    // 404 so callers cannot enumerate templates, and authorization always precedes
    // body validation for the same reason. Writes that fail inside the cache (folder
    // without products, partial per-sensor policy removal) are 409 + ProblemDetails;
    // request-shape problems are 400 + ValidationProblemDetails. The global exception
    // handler renders Razor HTML, so nothing is allowed to throw out of an action.
    [ApiController]
    [ManagementApi]
    [Authorize(Policy = HsmApiTokenDefaults.ManagementPolicy)]
    [Route("api/v1/alertTemplates")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public sealed class AlertTemplatesApiController : ControllerBase
    {
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 200;

        // Size guard rails the web UI gets from its widgets; the API states them
        // explicitly. Generous enough that no legitimate template hits them, tight
        // enough that a write token cannot bloat LevelDB or the per-sensor apply.
        public const int MaxNameLength = 200;
        public const int MaxPaths = 100;
        public const int MaxPolicies = 100;

        private readonly ITreeValuesCache _cache;
        private readonly IFolderManager _folders;
        private readonly IChatsManager _chats;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly ILogger<AlertTemplatesApiController> _logger;

        public AlertTemplatesApiController(ITreeValuesCache cache, IFolderManager folders,
            IChatsManager chats, IApiTokenAuthorizationService authorization,
            ILogger<AlertTemplatesApiController> logger)
        {
            _cache = cache;
            _folders = folders;
            _chats = chats;
            _authorization = authorization;
            _logger = logger;
        }


        [HttpGet]
        public IActionResult GetTemplates(int page = 1, int pageSize = DefaultPageSize)
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize <= 0 ? DefaultPageSize : pageSize, 1, MaxPageSize);

            // The list returns full entity bodies, so reach alone is not enough: an
            // item is listed only under the SAME operation its item endpoint would
            // demand (alerts:read) — a token granted only, say, history:read at the
            // folder gets a 403 on GET {id}, so the list must not disclose the item
            // either. Never 403-per-item: ungranted folders are simply not listed.
            // The decision is memoized per DISTINCT folder (templates cluster into a
            // handful of folders, and the evaluator recomputes user + token + grants
            // on every call); IsVisible records nothing, unlike per-item Authorize.
            var decisionByFolder = new Dictionary<Guid, bool>();

            bool IsListable(Guid folderId) =>
                decisionByFolder.TryGetValue(folderId, out var listable)
                    ? listable
                    : decisionByFolder[folderId] = _authorization.IsVisible(User, ApiTokenOperations.AlertsRead, FolderResource(folderId));

            var visible = (_cache.GetAlertTemplateModels() ?? [])
                .Where(t => IsListable(t.FolderId))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Id)
                .ToList();

            var totalPages = visible.Count == 0 ? 0 : (int)Math.Ceiling(visible.Count / (double)pageSize);

            // Clamp the page into [1, totalPages]: unchecked (page - 1) * pageSize
            // would overflow int for huge page numbers, and a NEGATIVE Skip count
            // silently returns the FIRST page labeled as page N.
            page = Math.Min(page, Math.Max(totalPages, 1));

            return Ok(new AlertTemplatePageDto
            {
                Items = [.. visible.Skip((page - 1) * pageSize).Take(pageSize).Select(AlertTemplateDtoMapper.ToDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = visible.Count,
                TotalPages = totalPages,
            });
        }

        [HttpGet("{id:guid}")]
        public IActionResult GetTemplate(Guid id)
        {
            var template = _cache.GetAlertTemplate(id);

            if (template is null)
                return NotFound();

            var failure = AuthorizeFolder(ApiTokenOperations.AlertsRead, template.FolderId);

            return failure ?? Ok(AlertTemplateDtoMapper.ToDto(template));
        }

        [HttpPost]
        public async Task<IActionResult> CreateTemplate([FromBody] AlertTemplateDto dto, CancellationToken ct)
        {
            // An all-zero folder id references no folder, so a 400 leaks nothing — and
            // it keeps the folderId structural check reachable instead of shadowed by
            // the evaluator's 404 for Guid.Empty.
            if (dto.FolderId == Guid.Empty)
                return FolderIdRequired();

            // The authorization target is the requested folder; no validation error is
            // reported before this decision (404-first).
            var failure = AuthorizeFolder(ApiTokenOperations.AlertsWrite, dto.FolderId);

            if (failure is not null)
                return failure;

            // The client id is IGNORED on create and the server generates a fresh one:
            // the cache Add is an upsert by id, so honoring a client-chosen id would let
            // a folder-scoped token overwrite a template in a folder it cannot even see.
            if (!TryBuildValidatedTemplate(dto, id: Guid.NewGuid(), isCreate: true, out var model, out var errors))
                return ValidationProblem(new ValidationProblemDetails(errors));

            var write = await TryWriteAsync(token => _cache.AddAlertTemplateAsync(model, token), ct);

            if (write is null)
                return ClientClosedRequest();

            if (!write.Value.Success)
                return ConflictProblem(write.Value.Error);

            return CreatedAtAction(nameof(GetTemplate), new { id = model.Id }, AlertTemplateDtoMapper.ToDto(model));
        }

        [HttpPut("{id:guid}")]
        public async Task<IActionResult> UpdateTemplate(Guid id, [FromBody] AlertTemplateDto dto, CancellationToken ct)
        {
            var existing = _cache.GetAlertTemplate(id);

            if (existing is null)
                return NotFound();

            // A template write is an indirect policy write on every matching sensor of
            // the target folder: moving a template needs write on BOTH folders — the
            // current one (the move destroys the old folder's per-sensor policies) and
            // the new one (the template injects policies into its sensors).
            var failure = AuthorizeFolder(ApiTokenOperations.AlertsWrite, existing.FolderId);

            if (failure is not null)
                return failure;

            // AFTER authorization (a body-shape 400 must not reveal that the template
            // exists to a caller outside its reach): an absent folderId is a 400, not a
            // 404 — Guid.Empty would otherwise fail boundary resolution in the
            // move-check below and shadow the structural check.
            if (dto.FolderId == Guid.Empty)
                return FolderIdRequired();

            if (dto.FolderId != existing.FolderId)
            {
                failure = AuthorizeFolder(ApiTokenOperations.AlertsWrite, dto.FolderId);

                if (failure is not null)
                    return failure;
            }

            if (!TryBuildValidatedTemplate(dto, id, isCreate: false, out var model, out var errors))
                return ValidationProblem(new ValidationProblemDetails(errors));

            var write = await TryWriteAsync(token => _cache.AddAlertTemplateAsync(model, token), ct);

            if (write is null)
                return ClientClosedRequest();

            if (!write.Value.Success)
                return ConflictProblem(write.Value.Error);

            // Echo the STORED template, not the request: the write normalizes ids and
            // chat names, and the caller must see the canonical result.
            var stored = _cache.GetAlertTemplate(id) ?? model;

            return Ok(AlertTemplateDtoMapper.ToDto(stored));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> DeleteTemplate(Guid id, CancellationToken ct)
        {
            var template = _cache.GetAlertTemplate(id);

            if (template is null)
                return NotFound();

            var failure = AuthorizeFolder(ApiTokenOperations.AlertsWrite, template.FolderId);

            if (failure is not null)
                return failure;

            var write = await TryWriteAsync(token => _cache.RemoveAlertTemplateAsync(id, token), ct);

            if (write is null)
                return ClientClosedRequest();

            return write.Value.Success ? NoContent() : ConflictProblem(write.Value.Error);
        }


        private static ApiTokenResource FolderResource(Guid folderId) =>
            new(ApiTokenResourceKind.Folder, folderId);

        // Maps the evaluator's decision onto the documented status codes: null when
        // allowed, an explicit 403 ProblemDetails when the folder is in reach but the
        // operation is not granted, a bare 404 when the folder is invisible or
        // out-of-reach (indistinguishable from absent). Forbid() is NOT used: it would
        // engage the (cookie) default scheme's forbidden handling — a redirect.
        private IActionResult AuthorizeFolder(string operation, Guid folderId)
        {
            var decision = _authorization.Authorize(User, operation, FolderResource(folderId));

            return decision switch
            {
                ApiTokenAuthorization.Allowed => null,
                ApiTokenAuthorization.Forbidden => Problem(statusCode: 403,
                    detail: $"The token does not grant '{operation}' at this folder."),
                _ => NotFound(),
            };
        }

        private ObjectResult ConflictProblem(string error) =>
            Problem(statusCode: 409, detail: error);

        private IActionResult FolderIdRequired() =>
            ValidationProblem(new ValidationProblemDetails(
                new Dictionary<string, string[]> { ["folderId"] = ["The folder id is required."] }));

        private static StatusCodeResult ClientClosedRequest() =>
            new(StatusCodes.Status499ClientClosedRequest);

        // The cache rethrows OperationCanceledException from its reconciliation loops.
        // With the client already gone the status is moot, but the exception must not
        // escape the action — the global handler renders Razor HTML. null result =>
        // 499. Note the write is NOT transactional at the cancellation point: a
        // cancelled reconcile can leave the template persisted with only some sensors
        // reconciled (pre-existing cache behavior, same for the web UI).
        private static async Task<(bool Success, string Error)?> TryWriteAsync(
            Func<CancellationToken, Task<(bool Success, string Error)>> write, CancellationToken ct)
        {
            try
            {
                return await write(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return null;
            }
        }

        // Structural validation, then entity reconstruction (inside a try — the domain
        // throws on legal-looking but unsupported input), then the semantic checks
        // ported from the web UI controller. Any accumulated error fails the request.
        private bool TryBuildValidatedTemplate(AlertTemplateDto dto, Guid id, bool isCreate,
            out AlertTemplateModel model, out Dictionary<string, string[]> errors)
        {
            var errorMap = new Dictionary<string, List<string>>();

            AddStructuralErrors(dto, id, isCreate, errorMap);

            // One pass over the chat manager serves both the mapper (canonical display
            // names) and the semantic chat-availability rule.
            var chatsById = ChatIndex();

            model = null;

            if (errorMap.Count == 0)
            {
                try
                {
                    model = new AlertTemplateModel(AlertTemplateDtoMapper.ToEntity(dto, id, CanonicalChatNames(chatsById)));
                }
                catch (Exception e)
                {
                    // e.g. Policy.Apply throws NotImplementedException for a condition
                    // property the sensor-type policy does not support. A 400 with a
                    // generic message — never a 500 out of the API area.
                    _logger.LogWarning(e, "API alert-template payload could not be reconstructed");
                    Add(errorMap, "policies", "The template could not be parsed: a condition is not supported for this sensor type.");
                }
            }

            if (model is not null)
                AddSemanticErrors(dto, model, id, errorMap, chatsById);

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

            if (dto.FolderId == Guid.Empty)
                Add(errors, "folderId", "The folder id is required.");

            if (dto.SensorType != AlertTemplateModel.AnyType && !Enum.IsDefined<SensorType>((SensorType)dto.SensorType))
                Add(errors, "sensorType", $"Unknown sensor type {dto.SensorType}.");

            if ((dto.TtlPolicies?.Count ?? 0) != (dto.Ttls?.Count ?? 0))
                Add(errors, "ttlPolicies", "ttlPolicies and ttls must be parallel lists of the same length.");

            if (!isCreate && dto.Id != Guid.Empty && dto.Id != id)
                Add(errors, "id", "The body id must match the route id.");

            // Null list elements are rejected HERE, before the domain reconstruction:
            // System.Text.Json materialises "policies": [null] happily, and a null
            // dereference inside the mapper would otherwise escape the action as a
            // 500 (the structural pass runs outside the reconstruction try).
            foreach (var policy in dto.Policies ?? [])
            {
                if (policy is null)
                {
                    Add(errors, "policies", "A policy entry must not be null.");
                    continue;
                }

                AddPolicyStructureErrors(policy, errors);
            }

            foreach (var policy in dto.TtlPolicies ?? [])
            {
                if (policy is null)
                {
                    Add(errors, "ttlPolicies", "A TTL policy entry must not be null.");
                    continue;
                }

                AddPolicyStructureErrors(policy, errors);
            }

            foreach (var interval in dto.Ttls ?? [])
                if (interval is null)
                    Add(errors, "ttls", "An interval entry must not be null.");
        }

        // Every enum byte is validated BEFORE the domain casts it: the entity
        // reconstruction casts without checks and unknown values either throw or
        // silently coerce (SensorStatus -> Error).
        private static void AddPolicyStructureErrors(AlertPolicyDto policy, Dictionary<string, List<string>> errors)
        {
            foreach (var condition in policy.Conditions ?? [])
            {
                if (condition?.Target is null)
                {
                    Add(errors, "conditions", "Every condition must carry a target.");
                    continue;
                }

                if (!Enum.IsDefined<PolicyOperation>((PolicyOperation)condition.Operation))
                    Add(errors, "conditions", $"Unknown condition operation {condition.Operation}.");

                if (!Enum.IsDefined<PolicyProperty>((PolicyProperty)condition.Property))
                    Add(errors, "conditions", $"Unknown condition property {condition.Property}.");

                if (!Enum.IsDefined<PolicyCombination>((PolicyCombination)condition.Combination))
                    Add(errors, "conditions", $"Unknown condition combination {condition.Combination}.");

                if (!Enum.IsDefined<TargetType>((TargetType)condition.Target.Type))
                    Add(errors, "conditions", $"Unknown condition target type {condition.Target.Type}.");
            }

            if (!Enum.IsDefined<SensorStatus>((SensorStatus)policy.SensorStatus))
                Add(errors, "sensorStatus", $"Unknown sensor status {policy.SensorStatus}.");

            if (!Enum.IsDefined<AlertRepeatMode>((AlertRepeatMode)(policy.Schedule?.RepeateMode ?? 0)))
                Add(errors, "schedule", $"Unknown repeat mode {policy.Schedule?.RepeateMode}.");

            var timeTicks = policy.Schedule?.TimeTicks ?? 0;

            if (timeTicks < 0 || timeTicks > DateTime.MaxValue.Ticks)
                Add(errors, "schedule", "schedule.timeTicks is outside the supported range.");

            if (policy.Destination?.Chats is { Count: > 0 } chats)
                foreach (var chatKey in chats.Keys)
                    if (!Guid.TryParse(chatKey, out _))
                        Add(errors, "destination", $"The chat id '{chatKey}' is not a valid Guid.");
        }

        // Semantic checks ported from the cookie controller (same order, same error
        // strings) plus the chat-availability rule the web UI enforces through its
        // dropdown: a chat is offerable when it is global or bound to the template's
        // folder. Runs only after authorization and structural validation passed.
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

            AddChatAvailabilityErrors(dto, errors, chatsById);
        }

        // Destination chat ids of every policy (regular and TTL) must resolve to a chat
        // that is global (bound to no folder) or bound to the template's folder — the
        // same predicate the web UI's chat dropdown applies. Runs on the DTO side: the
        // mapper preserves chat keys 1:1, and the semantic phase still owns the rule.
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

        private static Dictionary<string, string> CanonicalChatNames(Dictionary<string, Chat> chatsById)
        {
            var names = new Dictionary<string, string>();

            foreach (var (chatId, chat) in chatsById)
                names[chatId] = chat.Name;

            return names;
        }

        private static void Add(Dictionary<string, List<string>> errors, string key, string message)
        {
            (errors.TryGetValue(key, out var list) ? list : errors[key] = []).Add(message);
        }
    }
}
