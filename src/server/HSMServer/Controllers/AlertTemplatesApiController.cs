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
using HSMServer.Model.ManagementApi.AlertTemplates;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Authorization;
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

            // Out-of-reach templates are simply not listed, never 403-per-item — the
            // evaluator's sanctioned list predicate.
            var visible = (_cache.GetAlertTemplateModels() ?? [])
                .Where(t => _authorization.IsVisible(User, FolderResource(t.FolderId)))
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(t => t.Id)
                .ToList();

            return Ok(new AlertTemplatePageDto
            {
                Items = [.. visible.Skip((page - 1) * pageSize).Take(pageSize).Select(AlertTemplateDtoMapper.ToDto)],
                Page = page,
                PageSize = pageSize,
                TotalCount = visible.Count,
                TotalPages = visible.Count == 0 ? 0 : (int)Math.Ceiling(visible.Count / (double)pageSize),
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

            var (success, error) = await _cache.AddAlertTemplateAsync(model, ct);

            if (!success)
                return ConflictProblem(error);

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
            var failure = AuthorizeFolder(ApiTokenOperations.AlertsWrite, existing.FolderId) ??
                (dto.FolderId != existing.FolderId ? AuthorizeFolder(ApiTokenOperations.AlertsWrite, dto.FolderId) : null);

            if (failure is not null)
                return failure;

            if (!TryBuildValidatedTemplate(dto, id, isCreate: false, out var model, out var errors))
                return ValidationProblem(new ValidationProblemDetails(errors));

            var (success, error) = await _cache.AddAlertTemplateAsync(model, ct);

            if (!success)
                return ConflictProblem(error);

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

            var (success, error) = await _cache.RemoveAlertTemplateAsync(id, ct);

            return success ? NoContent() : ConflictProblem(error);
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

        // Structural validation, then entity reconstruction (inside a try — the domain
        // throws on legal-looking but unsupported input), then the semantic checks
        // ported from the web UI controller. Any accumulated error fails the request.
        private bool TryBuildValidatedTemplate(AlertTemplateDto dto, Guid id, bool isCreate,
            out AlertTemplateModel model, out Dictionary<string, string[]> errors)
        {
            var errorMap = new Dictionary<string, List<string>>();

            AddStructuralErrors(dto, id, isCreate, errorMap);

            model = null;

            if (errorMap.Count == 0)
            {
                try
                {
                    model = new AlertTemplateModel(AlertTemplateDtoMapper.ToEntity(dto, id, CanonicalChatNames()));
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
                AddSemanticErrors(dto, model, id, errorMap);

            errors = errorMap.ToDictionary(p => p.Key, p => p.Value.ToArray());

            return errors.Count == 0;
        }

        private void AddStructuralErrors(AlertTemplateDto dto, Guid id, bool isCreate,
            Dictionary<string, List<string>> errors)
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                Add(errors, "name", "The name is required.");

            if (dto.Paths is null || dto.Paths.All(string.IsNullOrWhiteSpace))
                Add(errors, "paths", "At least one path template is required.");

            if (dto.FolderId == Guid.Empty)
                Add(errors, "folderId", "The folder id is required.");

            if (dto.SensorType != AlertTemplateModel.AnyType && !Enum.IsDefined<SensorType>((SensorType)dto.SensorType))
                Add(errors, "sensorType", $"Unknown sensor type {dto.SensorType}.");

            if ((dto.TtlPolicies?.Count ?? 0) != (dto.Ttls?.Count ?? 0))
                Add(errors, "ttlPolicies", "ttlPolicies and ttls must be parallel lists of the same length.");

            if (!isCreate && dto.Id != Guid.Empty && dto.Id != id)
                Add(errors, "id", "The body id must match the route id.");

            foreach (var policy in (dto.Policies ?? []).Concat(dto.TtlPolicies ?? []))
                AddPolicyStructureErrors(policy, errors);
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
            Dictionary<string, List<string>> errors)
        {
            // Global and case-sensitive, exactly like the web UI.
            if (_cache.GetAlertTemplateModels()?.Any(x => x.Name == model.Name && x.Id != id) == true)
                Add(errors, "name", "The name must be unique.");

            if (!model.TryApplyPathTemplates(out var pathError))
                Add(errors, "paths", $"Invalid path template: {pathError}");

            foreach (var mismatchError in GetPathTypeMismatchErrors(model))
                Add(errors, "paths", mismatchError);

            AddChatAvailabilityErrors(dto, errors);
        }

        // #1210: a path template matching sensors of a type incompatible with the
        // template's concrete type is rejected — anything flagged here would be
        // silently skipped at apply time (AlertTemplateModel.IsMatch). AnyType
        // templates match every type and skip the check. A path matching nothing is
        // allowed (the template may precede its sensors).
        private List<string> GetPathTypeMismatchErrors(AlertTemplateModel model)
        {
            var errors = new List<string>();
            var templateType = model.GetSensorType();

            if (!templateType.HasValue)
                return errors;

            var templateTypeName = templateType.Value.ToString().Replace("Sensor", string.Empty);

            foreach (var path in model.Paths.Where(p => !string.IsNullOrWhiteSpace(p)))
            {
                var mismatched = _cache.GetSensors(path, null, model.FolderId)
                    .FirstOrDefault(s => s.Type != templateType.Value);

                if (mismatched != null)
                {
                    var mismatchedTypeName = mismatched.Type.ToString().Replace("Sensor", string.Empty);
                    errors.Add($"Path \"{path}\" matches {mismatchedTypeName} sensors, but this template is configured for {templateTypeName} sensors. Use a separate Alert Template for another sensor type.");
                }
            }

            return errors;
        }

        // Destination chat ids of every policy (regular and TTL) must resolve to a chat
        // that is global (bound to no folder) or bound to the template's folder — the
        // same predicate the web UI's chat dropdown applies. Runs on the DTO side: the
        // mapper preserves chat keys 1:1, and the semantic phase still owns the rule.
        private void AddChatAvailabilityErrors(AlertTemplateDto dto, Dictionary<string, List<string>> errors)
        {
            var chatsById = new Dictionary<string, Chat>();

            foreach (var chat in _chats.GetValues() ?? [])
                chatsById[chat.Id.ToString()] = chat;

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

        private Dictionary<string, string> CanonicalChatNames()
        {
            var names = new Dictionary<string, string>();

            foreach (var chat in _chats.GetValues() ?? [])
                names[chat.Id.ToString()] = chat.Name;

            return names;
        }

        private static void Add(Dictionary<string, List<string>> errors, string key, string message)
        {
            (errors.TryGetValue(key, out var list) ? list : errors[key] = []).Add(message);
        }
    }
}
