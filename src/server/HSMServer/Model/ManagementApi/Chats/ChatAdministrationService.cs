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
    /// The write engine behind the chat web flows (#1825) and the management surface
    /// (#1829): create (idempotent towards a row pre-created by the Telegram /start flow),
    /// partial update with the explicit channel-clear flags, the chat name rule
    /// (<see cref="ChatNameValidator"/>), webhook URL-shape validation in one place, and the
    /// chat-folder binding under the "only folders the caller manages" rule. Transport-
    /// agnostic: it answers the area's write envelope (<see cref="PolicyWriteResult"/>),
    /// never an IActionResult. Admission is the caller's — the web controller's filters and
    /// the management surface's service decide who may write; the engine decides what a
    /// valid write is.
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
            var stamped = Stamp(update, initiator);
            var withId = stamped.Id == Guid.Empty ? stamped with { Id = Guid.NewGuid() } : stamped;

            _chats.TryGetValue(withId.Id, out var existing);

            if (existing is not null)
                withId = KeepingItsStoredName(withId, existing);

            // The row may already exist (pre-created by /start): a name it already holds is no clash.
            if (Validate(withId, nameRequired: true, existing?.Name) is { } invalid)
                return invalid;

            Chat chat;

            if (existing is not null)
            {
                if (!await _chats.TryUpdate(withId))
                    return RefusedWrite(withId, nameRequired: true, existing.Name);

                chat = existing;
            }
            else
            {
                chat = BuildChat(withId, authorId);

                if (!await _chats.TryAdd(chat))
                    return RefusedWrite(withId, nameRequired: true);
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
            var stamped = Stamp(update, initiator);

            if (!_chats.TryGetValue(stamped.Id, out var chat))
                return PolicyWriteResult.Fail(PolicyWriteOutcome.NotFound);

            stamped = KeepingItsStoredName(stamped, chat);

            if (Validate(stamped, currentName: chat.Name) is { } invalid)
                return invalid;

            if (!await _chats.TryUpdate(stamped))
                return RefusedWrite(stamped, currentName: chat.Name);

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


        // The write rules of both surfaces: the chat name (#1829 — required on a creation,
        // at most 100 characters, unique per server ignoring case and surrounding spaces; an
        // omitted name, a partial update, is not checked, and a row keeping its own name
        // breaks nothing — compared trimmed, like every stored name), the aggregation delay
        // (0..a day — a negative delay never flushes a channel's accumulator, so a chat
        // holding one drops its alerts in silence), and the webhook
        // URL-shape rule the web form enforced (NotificationsController's
        // ValidateWebhooks, #1329): a non-null webhook field of a write must be an absolute
        // http/https URL. Null means "no change" and passes. Every problem answers in one
        // body, keyed by the update's property names — the API's wire names in camelCase,
        // and the web form's own where they coincide (the form carries the delay rule as a
        // Range attribute under its own field name). Internal: the management surface runs
        // the same validation first, to answer a request's every problem in one body before
        // the engine applies anything — it passes the row's current name as the engine does.
        internal PolicyWriteResult Validate(ChatUpdate update, bool nameRequired = false, string currentName = null)
        {
            Dictionary<string, string[]> errors = null;
            var name = nameRequired ? update.Name ?? string.Empty : update.Name;

            if (name is not null && ChatNameValidator.FindError(name, update.Id, _chats.GetValues() ?? [], currentName) is { } nameError)
                (errors ??= []).Add(nameof(ChatUpdate.Name), [nameError]);

            if (update.MessagesAggregationTimeSec is { } delay
                && (delay < Chat.MinMessagesAggregationTimeSec || delay > Chat.MaxMessagesAggregationTimeSec))
            {
                (errors ??= []).Add(nameof(ChatUpdate.MessagesAggregationTimeSec), [Chat.AggregationTimeRangeText]);
            }

            ValidateWebhook(update.SlackWebhookUrl, nameof(ChatUpdate.SlackWebhookUrl));
            ValidateWebhook(update.MattermostWebhookUrl, nameof(ChatUpdate.MattermostWebhookUrl));

            return errors is null
                ? null
                : PolicyWriteResult.Fail(PolicyWriteOutcome.Invalid, errors);

            void ValidateWebhook(string url, string field)
            {
                if (url is null)
                    return;

                if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                    || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    (errors ??= []).Add(field, ["Webhook URL must be a valid URL"]);
            }
        }

        // The write's initiator on the update, and the name trimmed — it is stored exactly
        // as it was validated.
        private static ChatUpdate Stamp(ChatUpdate update, InitiatorInfo initiator) =>
            update with { Initiator = initiator, Name = update.Name?.Trim() };

        // A posted name that is the row's stored name once trimmed is no change: the web
        // form shows a row stored with surrounding spaces (from before the name was held
        // trimmed) trimmed, and posts that back on an untouched save. The stored name is
        // kept as it is, so the row is neither refused as a clash with another chat's name
        // nor renamed — and journaled as renamed — behind the user's back.
        private static ChatUpdate KeepingItsStoredName(ChatUpdate update, Chat chat) =>
            update.Name is not null && ChatNameValidator.SameName(update.Name, chat.Name)
                ? update with { Name = chat.Name }
                : update;

        // A write the store refused. The store repeats the name rule in front of its own
        // write (ChatsManager), so this is where two writes of one name end: the loser's
        // write is refused there and, back here, the winner is in the store. The rules are
        // run once more against the store as it is NOW: a name that is taken answers the
        // same 422 as up front, naming the field. Nothing else can have changed since the
        // write passed the same rules a moment ago, so a refusal that still breaks none of
        // them is the store's own failure — the fixed-message conflict.
        private PolicyWriteResult RefusedWrite(ChatUpdate update, bool nameRequired = false, string currentName = null) =>
            Validate(update, nameRequired, currentName)
            ?? PolicyWriteResult.Fail(PolicyWriteOutcome.Conflict, message: ChatNotSavedMessage);

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

        // The old SyncFolders rule (the controller's, before #1825). Adds come ONLY from the Add
        // half: a kept row whose binding another admin removed while the form was open stays
        // unbound — kept rows decide removals, they never re-bind (the #1840 review). Removals
        // are the managed currently-bound folders missing from the kept rows; folders outside
        // the managed set are never touched (they keep their binding as it is). Adds first,
        // then removals, the web flow's order. Two write-level differences from SyncFolders
        // remain, neither changing an honest post's result: an Add folder the chat is already
        // bound to is not rewritten, and the chat is the instance the caller holds, not a
        // fresh read (they diverge only when the chat is removed mid-write). Its Folders is
        // the live index, snapshotted before diffing (the read service's rule — it mutates
        // from concurrent binding edits without a lock).
        private async Task BindFoldersAsync(Chat chat, ChatFolderBinding binding, InitiatorInfo initiator)
        {
            var chatId = chat.Id;
            var managed = binding.Managed;
            var current = StableCopy.Of(() => chat.Folders);

            foreach (var folderId in binding.Add.Where(managed.Contains).Where(id => !current.Contains(id)))
                if (_folders.TryGetValue(folderId, out var folder))
                    await UpdateFolderChats(folderId, [.. folder.Chats, chatId], initiator);

            foreach (var folderId in current.Except(binding.Keep).Where(managed.Contains))
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
    /// The folder binding of a chat write. Keep: the bindings the caller still wants (the edit
    /// form's kept rows) — they decide only removals. Add: the folders to bind (the form's
    /// SelectedFolders half) — the only source of new bindings. Managed: the folders the
    /// caller manages. Adds touch Add ∩ Managed; removals touch (the chat's current folders
    /// minus Keep) ∩ Managed — folders the caller cannot manage are never touched. A caller
    /// holding one desired list passes it as both Keep and Add. Managed is the scope the
    /// engine applies, not something it derives: it must be built from the authenticated
    /// caller's own folders, never taken from request data.
    /// </summary>
    public sealed record ChatFolderBinding(IReadOnlyCollection<Guid> Keep, IReadOnlyCollection<Guid> Add, IReadOnlyCollection<Guid> Managed);
}
