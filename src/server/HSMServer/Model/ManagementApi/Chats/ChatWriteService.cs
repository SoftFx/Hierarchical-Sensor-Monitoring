using HSMServer.Authentication;
using HSMServer.ConcurrentStorage;
using HSMServer.Core.Journal;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.ChangeSets;
using HSMServer.Notifications.Chats;
using HSMServer.Validation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// The chat write surface of the management API (#1829): create, update and delete on
    /// the write engine the web UI runs (<see cref="ChatAdministrationService"/>). This
    /// service owns what the web controller's filters and form do for a cookie user —
    /// admission by the token's owner, the folder scope, the wire shape of a rejection — and
    /// re-implements no write rule. Transport-agnostic: the REST controller and the MCP
    /// tools both call it and get the area's write envelope back, never an IActionResult.
    /// </summary>
    // Admission mirrors the web editor's gates (TelegramRoleFilterBase):
    //  - create is admin-only (AuthorizeChatCreate) — a chat created with an attacker-
    //    supplied webhook would exfiltrate every alert sent to it;
    //  - update and delete take the chat's detail rule — an admin or the manager of a
    //    folder the chat is bound to — so a global chat is admin-only. The area's
    //    anti-enumeration split holds: an INVISIBLE chat (unknown, or bound only to
    //    folders the owner cannot see) answers the same 404 as an unknown one; only a
    //    visible chat the caller may not change answers 403.
    // Every write carries the token's owner: as the author of a new row, and as the
    // initiator the storage journals (the chat records of #1828).
    public sealed class ChatWriteService
    {
        private const string NotCreatedMessage = "The chat could not be created.";
        private const string NotUpdatedMessage = "The chat could not be updated.";
        private const string NotRemovedMessage = "The chat could not be removed.";
        private const string FolderNotManagedMessage = "Every folder must exist and be one the token's owner manages.";

        private readonly IChatsManager _chats;
        private readonly ChatAdministrationService _engine;
        private readonly IApiTokenAuthorizationService _authorization;
        private readonly IFolderManager _folders;
        private readonly IUserManager _users;
        private readonly IChatSensorUsageCalculator _usage;


        public ChatWriteService(IChatsManager chats, ChatAdministrationService engine, IApiTokenAuthorizationService authorization,
            IFolderManager folders, IUserManager users, IChatSensorUsageCalculator usage)
        {
            _chats = chats;
            _engine = engine;
            _authorization = authorization;
            _folders = folders;
            _users = users;
            _usage = usage;
        }


        /// <summary>
        /// Create a chat: admin-only. The id is minted here, so the echo can read the
        /// stored row back; the response never carries a webhook URL (the DTO answers
        /// presence flags).
        /// </summary>
        public async Task<PolicyWriteResult<ChatDto>> CreateChat(CreateChatRequestDto request, ClaimsPrincipal caller)
        {
            if (_authorization.AuthorizeChatCreate(caller) != ApiTokenAuthorization.Allowed || OwnerOf(caller) is not { } owner)
                return Forbidden<ChatDto>();

            var chatId = Guid.NewGuid();
            var update = ToUpdate(chatId, request);
            var folders = request?.Folders ?? [];

            if (Validate(update, nameRequired: true, folders, caller) is { } errors)
                return PolicyWriteResult<ChatDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            // A new chat has no binding to keep: the requested folders ARE its bindings. The
            // engine only ever touches the folders the owner manages.
            var result = await _engine.CreateChatAsync(update, new ChatFolderBinding(Keep: folders, Add: folders, Managed: ManagedFoldersOf(caller)),
                owner.Id, InitiatorInfo.AsUser(owner.Name));

            if (!result.Success)
                return PolicyWriteResult<ChatDto>.Fail(result.Failure.Outcome, ToWireKeys(result.Failure.Errors), result.Failure.Message);

            // The engine stored the row under the id it was given; a missing row here is
            // the fixed-message conflict, never an echo of a chat that does not exist.
            return _chats[chatId] is { } stored
                ? PolicyWriteResult<ChatDto>.Ok(Echo(stored, caller))
                : PolicyWriteResult<ChatDto>.Fail(PolicyWriteOutcome.Conflict, message: NotCreatedMessage);
        }

        /// <summary>
        /// Change a chat: a partial update — an omitted field does not change — for an
        /// admin or the manager of a folder the chat is bound to.
        /// </summary>
        public async Task<PolicyWriteResult<ChatDto>> UpdateChat(Guid chatId, UpdateChatRequestDto request, ClaimsPrincipal caller)
        {
            // Admission first (the area's rule: it precedes body validation), in the
            // anti-enumeration split: a chat outside the caller's sight is the same 404 as
            // an unknown one.
            if (_chats[chatId] is not { } chat || !ChatSight.IsVisible(caller, chat, _authorization))
                return PolicyWriteResult<ChatDto>.Fail(PolicyWriteOutcome.NotFound);

            if (!ChatSight.CanOperate(caller, chat, _authorization) || OwnerOf(caller) is not { } owner)
                return Forbidden<ChatDto>();

            var update = ToUpdate(chatId, request);
            var current = StableCopy.Of(() => chat.Folders);
            var listed = request?.Folders;

            if (Validate(update, nameRequired: false, listed ?? [], caller) is { } errors)
                return PolicyWriteResult<ChatDto>.Fail(PolicyWriteOutcome.Invalid, errors);

            // Folders omitted: nothing is added and nothing removed. Listed: the list is the
            // whole desired set among the folders the owner manages — kept and added alike —
            // so a managed binding not listed goes and an unmanaged one is never touched.
            var binding = new ChatFolderBinding(Keep: listed is { } desired ? desired : current, Add: listed ?? [],
                Managed: ManagedFoldersOf(caller));

            var result = await _engine.UpdateChatAsync(update, binding, InitiatorInfo.AsUser(owner.Name));

            if (!result.Success)
                return PolicyWriteResult<ChatDto>.Fail(result.Failure.Outcome, ToWireKeys(result.Failure.Errors), result.Failure.Message);

            return _chats[chatId] is { } stored
                ? PolicyWriteResult<ChatDto>.Ok(Echo(stored, caller))
                : PolicyWriteResult<ChatDto>.Fail(PolicyWriteOutcome.Conflict, message: NotUpdatedMessage);
        }

        /// <summary>
        /// Remove a chat — for an admin or the manager of a folder it is bound to — through
        /// the same removal the web UI's list runs: the storage's own removal events prune
        /// the chat's references from folders and alert policies. A dry run answers the
        /// usage preview and removes nothing.
        /// </summary>
        public async Task<PolicyWriteResult> DeleteChat(Guid chatId, ClaimsPrincipal caller, bool dryRun = false)
        {
            if (_chats[chatId] is not { } chat || !ChatSight.IsVisible(caller, chat, _authorization))
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            if (!ChatSight.CanOperate(caller, chat, _authorization) || OwnerOf(caller) is not { } owner)
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Forbidden, message: PolicyWriteResult.WriteDeniedMessage);

            if (dryRun)
                return PolicyWriteResult.OkPreview(PreviewOf(chat, caller));

            if (!await _chats.TryRemove(new RemoveRequest(chatId, InitiatorInfo.AsUser(owner.Name))))
            {
                // The honest-verification arm of the deletion pattern: a chat that vanished
                // on the way is the plain 404, anything else the fixed-message conflict.
                return _chats[chatId] is null
                    ? PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound)
                    : PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: NotRemovedMessage);
            }

            return PolicyWriteResult.Ok();
        }

        // The counter behind the web chat list's usage badge (skipped sensors make it a
        // lower bound, as on the badge) and the bindings the deletion would take with it —
        // only the ones the caller can see.
        private ChatDeletePreviewDto PreviewOf(Chat chat, ClaimsPrincipal caller)
        {
            var (counts, skipped) = _usage.Compute();

            return new ChatDeletePreviewDto
            {
                DryRun = true,
                Kind = JournalKinds.Chat,
                Operation = JournalOperations.Delete,
                EntityId = chat.Id.ToString(),
                Path = chat.Name,
                SensorUsageCount = counts.GetValueOrDefault(chat.Id),
                SensorUsageIncomplete = skipped > 0,
                Affected =
                [
                    new() { Id = chat.Id.ToString(), Path = chat.Name, Change = "remove" },
                    .. StableCopy.Of(() => chat.Folders)
                        .Where(folderId => _authorization.IsVisible(caller, ApiTokenResource.Folder(folderId)))
                        .Select(folderId => new PreviewAffectedEntityDto
                        {
                            Id = folderId.ToString(),
                            Path = _folders.TryGetValue(folderId, out var folder) ? folder.Name : folderId.ToString(),
                            Change = "unbind",
                        }),
                ],
            };
        }


        private static ChatUpdate ToUpdate(Guid chatId, UpdateChatRequestDto request) => new()
        {
            Id = chatId,
            Name = request?.Name?.Trim(),
            Description = request?.Description,
            SendMessages = request?.SendMessages,
            MessagesAggregationTimeSec = request?.MessagesAggregationTimeSec,
            SlackWebhookUrl = WebhookOf(request?.SlackWebhookUrl),
            MattermostWebhookUrl = WebhookOf(request?.MattermostWebhookUrl),
            ClearSlackWebhook = request?.ClearSlackWebhook,
            ClearMattermostWebhook = request?.ClearMattermostWebhook,
            ClearTelegramBinding = request?.ClearTelegramBinding,
        };


        private static ChatUpdate ToUpdate(Guid chatId, CreateChatRequestDto request) => new()
        {
            Id = chatId,
            Name = request?.Name?.Trim(),
            Description = request?.Description,
            SendMessages = request?.SendMessages,
            MessagesAggregationTimeSec = request?.MessagesAggregationTimeSec,
            SlackWebhookUrl = WebhookOf(request?.SlackWebhookUrl),
            MattermostWebhookUrl = WebhookOf(request?.MattermostWebhookUrl),
        };

        // An empty webhook value never clears a stored one (a read never returns the URL, so
        // there is nothing to echo back): it means "no change", like an omitted field.
        private static string WebhookOf(string url) => string.IsNullOrWhiteSpace(url) ? null : url.Trim();

        // Every problem of a request in one map, keyed by the wire names; null when the
        // request is clean. The write rules are the engine's (the name, the webhook URL
        // shape); this surface adds the two that have no meaning for a cookie user — the
        // #1705 character rule of a name, on the API/MCP paths only, and the folder scope.
        private Dictionary<string, string[]> Validate(ChatUpdate update, bool nameRequired, IReadOnlyCollection<Guid> folders,
            ClaimsPrincipal caller)
        {
            var errors = new Dictionary<string, List<string>>();

            void Add(string field, string message) =>
                (errors.TryGetValue(field, out var messages) ? messages : errors[field] = []).Add(message);

            if (TextFieldValidator.HasForbiddenCharacter(update.Name, out var found, allowLineBreaks: false))
                Add("name", $"The name must not contain control or bidirectional-override characters (found {found}).");

            // An unknown folder and a folder the owner cannot manage are the same refusal:
            // the answer discloses nothing about folders outside the owner's reach.
            if (folders.Any(folderId => !_folders.TryGetValue(folderId, out _) || !_authorization.CanSeeFolderDetail(caller, folderId)))
                Add("folders", FolderNotManagedMessage);

            if (_engine.Validate(update, nameRequired)?.Failure?.Errors is { } engineErrors)
                foreach (var (field, messages) in ToWireKeys(engineErrors))
                    errors[field] = [.. errors.GetValueOrDefault(field, []), .. messages];

            return errors.Count == 0 ? null : errors.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        }

        // The scope of every binding change: the folders the token's owner manages (the
        // evaluator's folder rule — an admin, or the folder's Manager).
        private HashSet<Guid> ManagedFoldersOf(ClaimsPrincipal caller) =>
            [.. _folders.GetValues().Where(folder => _authorization.CanSeeFolderDetail(caller, folder.Id)).Select(folder => folder.Id)];

        private ChatDto Echo(Chat chat, ClaimsPrincipal caller) =>
            ChatDtoMapper.ToDto(chat, folderId => _authorization.IsVisible(caller, ApiTokenResource.Folder(folderId)), includeDetail: true);

        private User OwnerOf(ClaimsPrincipal caller) =>
            Guid.TryParse(caller?.FindFirst(HsmApiTokenClaims.OwnerUserId)?.Value, out var ownerId) ? _users[ownerId] : null;

        private static PolicyWriteResult<T> Forbidden<T>() =>
            PolicyWriteResult<T>.Fail(PolicyWriteOutcome.Forbidden, message: PolicyWriteResult.WriteDeniedMessage);

        // The engine keys a rejection by the update's property names, which are the web
        // form's field keys; the wire names the same properties in camelCase.
        private static IDictionary<string, string[]> ToWireKeys(IDictionary<string, string[]> errors) =>
            errors?.ToDictionary(pair => WireKeyOf(pair.Key), pair => pair.Value);

        private static string WireKeyOf(string key) => char.ToLowerInvariant(key[0]) + key[1..];
    }
}
