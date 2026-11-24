using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Notifications.Chats;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace HSMServer.Model.ManagementApi.Chats
{
    /// <summary>
    /// The write engine behind the chat web flows (#1825): create (idempotent towards a row
    /// pre-created by the Telegram /start flow), partial update with the explicit channel-clear
    /// flags, webhook URL-shape validation in one place, and the chat-folder binding under the
    /// "only folders the caller manages" rule. The web controller delegates here; the
    /// /api/v1 + MCP surface (#1829) will call the same engine. Transport-agnostic: it answers
    /// the area's write envelope (<see cref="PolicyWriteResult"/>), never an IActionResult.
    /// </summary>
    public sealed class ChatAdministrationService
    {
        private const string ChatNotSavedMessage = "The chat could not be saved.";

        private readonly IChatsManager _chats;
        private readonly IFolderManager _folders;


        public ChatAdministrationService(IChatsManager chats, IFolderManager folders)
        {
            _chats = chats;
            _folders = folders;
        }


        /// <summary>
        /// The AddChat POST flow: when the row already exists (created by /start against the
        /// pre-allocated guid), the save becomes an update so it is idempotent w.r.t. the order
        /// of "fill the name" and "click setup help". A fresh row carries the creating user as
        /// its author; an empty id (a crafted post — the honest form pre-allocates) gets a
        /// server-generated one, as the old flow did.
        /// </summary>
        public async Task<PolicyWriteResult> CreateChatAsync(ChatUpdate update, ChatFolderBinding binding, Guid authorId, InitiatorInfo initiator)
        {
            var stamped = update with { Initiator = initiator };
            var withId = stamped.Id == Guid.Empty ? stamped with { Id = Guid.NewGuid() } : stamped;

            if (ValidateWebhooks(withId) is { } invalid)
                return invalid;

            Chat chat;

            if (_chats.TryGetValue(withId.Id, out var existing))
            {
                if (!await _chats.TryUpdate(withId))
                    return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: ChatNotSavedMessage);

                chat = existing;
            }
            else
            {
                chat = BuildChat(withId, authorId);

                if (!await _chats.TryAdd(chat))
                    return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: ChatNotSavedMessage);
            }

            await BindFoldersAsync(chat, binding, initiator);

            return PolicyWriteResult.Ok();
        }

        /// <summary>
        /// The EditChat POST flow: a partial update (an omitted field does not change; a channel
        /// is removed only by its explicit clear flag), then the folder binding.
        /// </summary>
        public async Task<PolicyWriteResult> UpdateChatAsync(ChatUpdate update, ChatFolderBinding binding, InitiatorInfo initiator)
        {
            var stamped = update with { Initiator = initiator };

            if (ValidateWebhooks(stamped) is { } invalid)
                return invalid;

            if (!_chats.TryGetValue(stamped.Id, out var chat))
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            if (!await _chats.TryUpdate(stamped))
                return PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: ChatNotSavedMessage);

            await BindFoldersAsync(chat, binding, initiator);

            return PolicyWriteResult.Ok();
        }

        /// <summary>
        /// The per-channel Remove buttons of the edit form: one clear flag, nothing else.
        /// </summary>
        public async Task<PolicyWriteResult> ClearChannelAsync(Guid chatId, ChatChannel channel, InitiatorInfo initiator)
        {
            var update = new ChatUpdate
            {
                Id = chatId,
                ClearTelegramBinding = channel is ChatChannel.Telegram ? true : null,
                ClearSlackWebhook = channel is ChatChannel.Slack ? true : null,
                ClearMattermostWebhook = channel is ChatChannel.Mattermost ? true : null,
                Initiator = initiator,
            };

            if (!_chats.TryGetValue(chatId, out _))
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            return await _chats.TryUpdate(update)
                ? PolicyWriteResult.Ok()
                : PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: ChatNotSavedMessage);
        }


        // The same webhook URL-shape rule the web form enforced (NotificationsController's
        // ValidateWebhooks, #1329), in one place now: a non-null webhook field of a write must
        // be an absolute http/https URL. Null means "no change" and passes. Field keys match
        // the form's property names, so the web re-render finds its spans.
        private static PolicyWriteResult ValidateWebhooks(ChatUpdate update)
        {
            Dictionary<string, string[]> errors = null;

            Validate(update.SlackWebhookUrl, nameof(ChatUpdate.SlackWebhookUrl));
            Validate(update.MattermostWebhookUrl, nameof(ChatUpdate.MattermostWebhookUrl));

            return errors is null
                ? null
                : PolicyWriteResult.Fail(PolicyWriteOutcome.Invalid, errors);

            void Validate(string url, string field)
            {
                if (url is null)
                    return;

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    (errors ??= []).Add(field, ["Webhook URL must be a valid URL"]);
            }
        }

        // A brand-new chat is built from the update the same way the web form built it
        // (ChatViewModel.ToNewChat): defaults for the omitted settings, author + creation time
        // stamped here. The Initiator field has no slot on a fresh row — the author IS the
        // creator; #1828's journal subscription reads the initiator from the update/clear
        // paths and the author from the row.
        private static Chat BuildChat(ChatUpdate update, Guid authorId) =>
            new(new ChatEntity
            {
                Id = update.Id.ToByteArray(),
                Author = authorId.ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = update.Name,
                Description = update.Description ?? string.Empty,
                SendMessages = update.SendMessages ?? Chat.DefaultSendMessages,
                MessagesAggregationTimeSec = update.MessagesAggregationTimeSec ?? Chat.DefaultMessagesAggregationTimeSec,
                SlackWebhookUrl = update.SlackWebhookUrl,
                MattermostWebhookUrl = update.MattermostWebhookUrl,
            });

        // The SyncFolders rule: add the desired folders the caller manages, remove the managed
        // ones that dropped out of the desired set — folders outside the managed set are never
        // touched (they keep their binding as it is). Adds first, then removals, matching the
        // web flow's order. Equivalent to the old SyncFolders on every honest form post (the
        // form's kept rows and newly picked folders never overlap); a crafted post naming one
        // folder in both halves keeps the binding instead of ending unbound — no honest flow
        // reaches that and no privilege is gained either way. The chat instance is the one the
        // caller already holds; its Folders is the live index, snapshotted before diffing (the
        // read service's rule — it mutates from concurrent binding edits without a lock).
        private async Task BindFoldersAsync(Chat chat, ChatFolderBinding binding, InitiatorInfo initiator)
        {
            var chatId = chat.Id;
            var managed = binding.Managed;
            var current = StableCopy.Of(() => chat.Folders);

            foreach (var folderId in binding.Desired.Where(managed.Contains).Where(id => !current.Contains(id)))
                if (_folders.TryGetValue(folderId, out var folder))
                    await UpdateFolderChats(folderId, [.. folder.Chats, chatId], initiator);

            foreach (var folderId in current.Except(binding.Desired).Where(managed.Contains))
                if (_folders.TryGetValue(folderId, out var folder))
                {
                    var folderChats = new HashSet<Guid>(folder.Chats);
                    folderChats.Remove(chatId);

                    await UpdateFolderChats(folderId, folderChats, initiator);
                }
        }

        private Task UpdateFolderChats(Guid folderId, HashSet<Guid> chats, InitiatorInfo initiator) =>
            _folders.TryUpdate(new FolderUpdate
            {
                Id = folderId,
                Chats = chats,
                Initiator = initiator,
            });
    }

    /// <summary>The delivery channel of a chat the clear action targets.</summary>
    public enum ChatChannel
    {
        Telegram,
        Slack,
        Mattermost,
    }

    /// <summary>
    /// The folder binding of a chat write: the folders the caller wants the chat bound to
    /// (Desired) and the folders the caller manages (Managed). Adds touch Desired ∩ Managed,
    /// removals touch (the chat's current folders minus Desired) ∩ Managed — folders the
    /// caller cannot manage are never touched. Managed is the scope the engine applies, not
    /// something it derives: it must be built from the authenticated caller's own folders,
    /// never taken from request data.
    /// </summary>
    public sealed record ChatFolderBinding(IReadOnlyCollection<Guid> Desired, IReadOnlyCollection<Guid> Managed);
}
