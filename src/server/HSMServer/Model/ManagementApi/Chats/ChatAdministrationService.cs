using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.TableOfChanges;
using HSMServer.Folders;
using HSMServer.Model.Folders;
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
    /// "only folders the caller manages" rule. Every write carries the initiator. The web
    /// controller delegates here; the /api/v1 + MCP surface (#1829) will call the same engine.
    /// Transport-agnostic: it works with the storage managers and answers a result, never an
    /// IActionResult.
    /// </summary>
    public sealed class ChatAdministrationService
    {
        private const string NotFound = "The requested resource was not found.";
        private const string WriteFailed = "The chat could not be saved.";

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
        /// of "fill the name" and "click setup help".
        /// </summary>
        public async Task<ChatWriteResult> CreateChatAsync(ChatUpdate update, ChatFolderBinding binding, Guid authorId, InitiatorInfo initiator)
        {
            var stamped = update with { Initiator = initiator };

            if (ValidateWebhooks(stamped) is { } invalid)
                return invalid;

            Chat chat;
            if (_chats.TryGetValue(stamped.Id, out var existing))
            {
                await _chats.TryUpdate(stamped);
                chat = existing;
            }
            else
            {
                chat = BuildChat(stamped, authorId);
                await _chats.TryAdd(chat);
            }

            await BindFoldersAsync(chat, binding, initiator);

            return ChatWriteResult.Ok();
        }

        /// <summary>
        /// The EditChat POST flow: a partial update (an omitted field does not change; a channel
        /// is removed only by its explicit clear flag), then the folder binding.
        /// </summary>
        public async Task<ChatWriteResult> UpdateChatAsync(ChatUpdate update, ChatFolderBinding binding, InitiatorInfo initiator)
        {
            var stamped = update with { Initiator = initiator };

            if (ValidateWebhooks(stamped) is { } invalid)
                return invalid;

            if (!_chats.TryGetValue(stamped.Id, out var chat))
                return ChatWriteResult.Fail(new ChatFieldError(nameof(ChatUpdate.Id), NotFound));

            if (!await _chats.TryUpdate(stamped))
                return ChatWriteResult.Fail(new ChatFieldError(nameof(ChatUpdate.Id), WriteFailed));

            await BindFoldersAsync(chat, binding, initiator);

            return ChatWriteResult.Ok();
        }

        /// <summary>
        /// The per-channel Remove buttons of the edit form: one clear flag, nothing else.
        /// </summary>
        public async Task<ChatWriteResult> ClearChannelAsync(Guid chatId, ChatChannel channel, InitiatorInfo initiator)
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
                return ChatWriteResult.Fail(new ChatFieldError(nameof(ChatUpdate.Id), NotFound));

            return await _chats.TryUpdate(update)
                ? ChatWriteResult.Ok()
                : ChatWriteResult.Fail(new ChatFieldError(nameof(ChatUpdate.Id), WriteFailed));
        }


        // The same webhook URL-shape rule the web form enforced (NotificationsController's
        // ValidateWebhooks, #1329), in one place now: a non-null webhook field of a write must
        // be an absolute http/https URL. Null means "no change" and passes.
        private static ChatWriteResult ValidateWebhooks(ChatUpdate update)
        {
            List<ChatFieldError> errors = null;

            Validate(update.SlackWebhookUrl, nameof(ChatUpdate.SlackWebhookUrl));
            Validate(update.MattermostWebhookUrl, nameof(ChatUpdate.MattermostWebhookUrl));

            return errors is null ? null : ChatWriteResult.Fail([.. errors]);

            void Validate(string url, string field)
            {
                if (url is null)
                    return;

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    (errors ??= []).Add(new ChatFieldError(field, "Webhook URL must be a valid URL"));
            }
        }

        // A brand-new chat is built from the update the same way the web form built it
        // (ChatViewModel.ToNewChat): defaults for the omitted settings, author + creation time
        // stamped here.
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
        // web flow's order. The chat instance is the one the caller already holds — its Folders
        // is the live index.
        private async Task BindFoldersAsync(Chat chat, ChatFolderBinding binding, InitiatorInfo initiator)
        {
            var chatId = chat.Id;
            var managed = binding.Managed;
            var current = chat.Folders;

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

    /// <summary>One field-keyed rejection (the domain property name + the reason).</summary>
    public sealed record ChatFieldError(string Field, string Error);

    /// <summary>The outcome of a chat write: success, or the field-keyed rejections.</summary>
    public sealed record ChatWriteResult(bool Success, IReadOnlyList<ChatFieldError> Errors = null)
    {
        public static ChatWriteResult Ok() => new(true);

        public static ChatWriteResult Fail(params ChatFieldError[] errors) => new(false, errors);
    }

    /// <summary>
    /// The folder binding of a chat write: the folders the caller wants the chat bound to
    /// (Desired) and the folders the caller manages (Managed). Add and remove touch only
    /// Desired ∩ Managed — folders the caller cannot manage are never touched.
    /// </summary>
    public sealed record ChatFolderBinding(IReadOnlyCollection<Guid> Desired, IReadOnlyCollection<Guid> Managed);
}
