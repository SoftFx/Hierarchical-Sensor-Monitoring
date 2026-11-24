using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Filters.FolderRoleFilters;
using HSMServer.Filters.TelegramRoleFilters;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.Configuration;
using HSMServer.Model.ManagementApi.Alerts;
using HSMServer.Model.ManagementApi.Chats;
using HSMServer.Model.Notifications;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace HSMServer.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class NotificationsController : BaseController
    {
        private readonly IFolderManager _folderManager;
        private readonly TelegramBot _telegramBot;
        private readonly NotificationsCenter _notifications;
        private readonly ChatSensorUsageCalculator _usageCalculator;
        private readonly ChatAdministrationService _chatEngine;

        internal IChatsManager ChatsManager { get; }


        public NotificationsController(IChatsManager chatsManager, NotificationsCenter notifications,
            IFolderManager folderManager, IUserManager userManager,
            ChatSensorUsageCalculator usageCalculator, ChatAdministrationService chatEngine) : base(userManager)
        {
            ChatsManager = chatsManager;
            _folderManager = folderManager;
            _notifications = notifications;
            _telegramBot = notifications.TelegramBot;
            _usageCalculator = usageCalculator;
            _chatEngine = chatEngine;
        }


        [HttpGet]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public IActionResult EditChat(Guid id, string tab = null)
        {
            if (!ChatsManager.TryGetValue(id, out var chat))
                return _emptyResult;

            ViewData["Tab"] = tab;
            return View(new ChatViewModel(chat, BuildChatFolders(chat)));
        }

        [HttpPost]
        [TelegramRoleFilterByEditModel(nameof(model), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> EditChat(ChatViewModel model)
        {
            // Load the stored chat so ValidateWebhooks can tell "the unchanged masked sentinel was
            // posted back" (expected, no change) from "the admin partially edited the masked value"
            // (must be rejected — see #1329 review: a partial edit used to be silently discarded).
            ChatsManager.TryGetValue(model.Id, out var stored);
            ValidateWebhooks(model, stored);

            // The write rules live in the engine (#1825): webhook URL shape, the partial update,
            // the managed-folder binding. Field errors land under the same ModelState keys the
            // form-level guards use, so the tab-routing below works unchanged. Only the Invalid
            // outcome carries field errors; a NotFound/Conflict keeps the OLD failed-write arm —
            // no folder mutation (the engine gates it), the admin lands on the list, exactly
            // what the pre-engine flow did when TryUpdate answered false.
            var engineResult = ModelState.IsValid
                ? await _chatEngine.UpdateChatAsync(model.ToUpdate(), BuildFolderBinding(model), CurrentInitiator)
                : null;
            ApplyEngineErrors(engineResult);

            if (!ModelState.IsValid)
            {
                RestoreStoredDisplayState(model, stored);

                // Land the user on the tab whose field actually has the error. EditChat.cshtml
                // renders asp-validation-for spans inside .tab-pane fade divs that are display:none
                // unless that tab is active; without this the default-tab heuristic (telegram → slack
                // → mattermost) hides the rejection on most chat configurations — e.g. a Telegram-
                // bound chat with a Slack webhook lands on the telegram tab and the Slack error is
                // invisible, recreating exactly the silent-rotation failure the guard exists to fix.
                SetTabFromWebhookErrors();

                return View(model);
            }

            // Index (the chats list) is [AuthorizeIsAdmin], but EditChat is also open to
            // ProductManagers of folders the chat is bound to. Redirecting everyone to Index
            // would 401 those non-admin PMs after a successful save. Send admins to the list
            // (matching AddChat); keep non-admins on the pre-fix re-render-from-DB path.
            if (CurrentUser.IsAdmin)
                return RedirectToAction(nameof(Index), ViewConstants.NotificationsController);

            return ChatsManager.TryGetValue(model.Id, out var chat)
                ? View(new ChatViewModel(chat, BuildChatFolders(chat)))
                : RedirectToAction(nameof(ProductController.Index), ViewConstants.ProductController);
        }

        [HttpGet]
        [AuthorizeIsAdmin]
        public IActionResult Index()
        {
            var (usageCounts, skipped) = _usageCalculator.Compute();
            return View(nameof(Index), new ChatsSettingsViewModel(ChatsManager, _folderManager, usageCounts, skipped > 0));
        }

        [HttpGet]
        [AuthorizeIsAdmin]
        // Pre-generate the chat guid so the EditChat form opens with a real Id. This lets the
        // Telegram bot-invite flow build an invitation token against this guid up-front; when the
        // user completes /start, TryConnect sees a chatId that is not yet in storage and creates
        // the Chat record on demand. No row is written until /start, so abandoning the form
        // leaves no orphan. IsNewChat flag tells the EditChat view to render "Add chat" copy and
        // submit to AddChat (POST) — `Id == Guid.Empty` no longer works as a new-chat signal
        // because of the pre-allocation.
        public IActionResult AddChat() => View(nameof(EditChat), new ChatViewModel { Id = Guid.NewGuid(), EnableMessages = true, IsNewChat = true });

        [HttpPost]
        [AuthorizeIsAdmin]
        public async Task<IActionResult> AddChat(ChatViewModel model)
        {
            // AddChat is for brand-new chats; a masked sentinel should never appear here (the new-chat
            // form renders empty webhook fields). Still, if /start pre-created the chat, load it so the
            // same partial-edit guard applies on the idempotent update path.
            ChatsManager.TryGetValue(model.Id, out var stored);
            ValidateWebhooks(model, stored);

            // The create-or-update idempotency (the row may already exist, pre-created by /start
            // against the pre-allocated guid) lives in the engine (#1825).
            var engineResult = ModelState.IsValid
                ? await _chatEngine.CreateChatAsync(model.ToUpdate(), BuildFolderBinding(model), CurrentUser.Id, CurrentInitiator)
                : null;
            ApplyEngineErrors(engineResult);

            if (!ModelState.IsValid)
            {
                // Same re-render restoration and tab-routing as EditChat POST — see there for why
                // the server-owned folder/display data must be repopulated and the failing
                // field's tab must become active (otherwise the rejection error is rendered
                // into a display:none pane).
                RestoreStoredDisplayState(model, stored);
                SetTabFromWebhookErrors();

                return View(nameof(EditChat), model);
            }

            return RedirectToAction(nameof(Index), ViewConstants.NotificationsController);
        }

        [HttpPost]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> RemoveChat(Guid id) =>
            await ChatsManager.TryRemove(new(id, CurrentInitiator)) ? Ok() : NotFound();

        [HttpGet]
        [FolderRoleFilterByFolderId(nameof(folderId), ProductRoleEnum.ProductManager)]
        public RedirectResult OpenInvitationLink(Guid folderId) =>
            Redirect(ChatsManager.GetInvitationLink(folderId, CurrentUser));

        [HttpGet]
        [FolderRoleFilterByFolderId(nameof(folderId), ProductRoleEnum.ProductManager)]
        public string GetGroupInvitation(Guid folderId) => ChatsManager.GetGroupInvitation(folderId, CurrentUser);

        [HttpGet]
        [TelegramRoleFilterById(nameof(chatId), ProductRoleEnum.ProductManager)]
        public RedirectResult OpenChatInvitationLink(Guid chatId) =>
            Redirect(ChatsManager.GetChatInvitationLink(chatId, CurrentUser));

        [HttpGet]
        [TelegramRoleFilterById(nameof(chatId), ProductRoleEnum.ProductManager)]
        public string GetChatGroupInvitation(Guid chatId) => ChatsManager.GetChatGroupInvitation(chatId, CurrentUser);

        [HttpGet]
        public async Task<IActionResult> OpenTelegramGroup(long chatId)
        {
            (var link, var error) = await _telegramBot.TryGetChatLink(chatId);

            return Json(new { link, error });
        }

        [HttpGet]
        public async ValueTask SendTestTelegramMessage(long chatId)
        {
            var testMessage = $"Test message for {CurrentUser.Name}.";

            await _telegramBot.SendTestMessageAsync(chatId, testMessage);
        }

        [HttpGet]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> SendTestSlackMessage([FromQuery] Guid id)
        {
            if (ChatsManager.TryGetValue(id, out var chat))
                await _notifications.SlackChannel.SendTestAsync(chat);

            return Ok();
        }

        [HttpGet]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> SendTestMattermostMessage([FromQuery] Guid id)
        {
            if (ChatsManager.TryGetValue(id, out var chat))
                await _notifications.MattermostChannel.SendTestAsync(chat);

            return Ok();
        }

        // Polled by EditChat: the /start binding completes async in the bot, outside any browser request.
        [HttpGet]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public IActionResult TelegramConnectionStatus(Guid id)
        {
            var connected = ChatsManager.TryGetValue(id, out var chat) && chat.TelegramChatId is not null;
            return Json(new { connected });
        }

        [HttpPost]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> RemoveTelegramBinding(Guid id) =>
            MapEngineResult(await _chatEngine.ClearChannelAsync(id, ChatChannel.Telegram, CurrentInitiator));

        [HttpPost]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> ClearSlackWebhook(Guid id) =>
            MapEngineResult(await _chatEngine.ClearChannelAsync(id, ChatChannel.Slack, CurrentInitiator));

        [HttpPost]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> ClearMattermostWebhook(Guid id) =>
            MapEngineResult(await _chatEngine.ClearChannelAsync(id, ChatChannel.Mattermost, CurrentInitiator));

        private IActionResult MapEngineResult(PolicyWriteResult result) =>
            result.Success ? Ok() : NotFound();

        // The binding the caller asks for (the folders the form keeps, plus the newly picked ones)
        // against the folders the caller manages — the engine applies the intersection rule.
        private ChatFolderBinding BuildFolderBinding(ChatViewModel model)
        {
            var managed = _folderManager.GetUserFolders(CurrentUser).Select(f => f.Id).ToHashSet();

            var desired = new HashSet<Guid>(model.Folders?.Folders ?? []);
            foreach (var selected in model.Folders?.SelectedFolders ?? [])
                desired.Add(selected);

            return new ChatFolderBinding(desired, managed);
        }

        // Only the Invalid outcome's field-keyed errors reach the form; the other outcomes keep
        // the pre-engine arms (the form has no spans for them and the old flow surfaced none).
        private void ApplyEngineErrors(PolicyWriteResult result)
        {
            if (result?.Failure?.Errors is not { Count: > 0 })
                return;

            foreach (var (field, messages) in result.Failure.Errors)
                foreach (var message in messages)
                    ModelState.AddModelError(field, message);
        }


        // Form-level webhook guards (#1329): the masked-sentinel partial-edit rejection and the
        // empty-field pointer to the per-channel Remove button. These need the POSTED form value
        // and the stored chat, so they stay with the form; the URL-SHAPE rule (absolute http/https)
        // lives in the write engine (#1825) — its field errors arrive through ApplyEngineErrors
        // under the same keys, so the tab-routing below treats them identically.
        private void ValidateWebhooks(ChatViewModel model, Chat stored)
        {
            ValidateWebhook(model.SlackWebhookUrl, stored?.SlackWebhookUrl, nameof(ChatViewModel.SlackWebhookUrl), "Slack");
            ValidateWebhook(model.MattermostWebhookUrl, stored?.MattermostWebhookUrl, nameof(ChatViewModel.MattermostWebhookUrl), "Mattermost");

            void ValidateWebhook(string posted, string storedUrl, string key, string channelName)
            {
                // MVC binds an empty text input to null via ConvertEmptyStringToNull, so both null
                // and "" reach here. Empty + stored webhook = point to Remove. Empty + no stored
                // webhook is legitimate (new chat, or an already-cleared channel) — MUST return
                // here, not fall through: the guards below assume a non-empty post.
                if (string.IsNullOrWhiteSpace(posted))
                {
                    if (!string.IsNullOrWhiteSpace(storedUrl))
                        ModelState.AddModelError(key, $"Use Remove {channelName} to delete the webhook.");

                    return;
                }

                // The masked sentinel must match Mask(stored) exactly, or the post is rejected: the
                // field is a plain editable input pre-filled with the mask, so an admin who partially
                // edits it (e.g. changes only the host) must be told — otherwise ToUpdate would
                // silently drop the edit because IsMasked is a substring test (#1329 review: a
                // rotation could appear to succeed while nothing changed). storedUrl uses
                // IsNullOrWhiteSpace to stay symmetric with Mask/ResolveWebhook — a whitespace-only
                // stored value would otherwise make the chat unsavable (the "Use Remove …" error
                // fires while the whitespace hides the Remove button via HasSlack).
                var maskError = WebhookUrlMasker.ValidatePosted(posted, storedUrl);
                if (maskError != null)
                    ModelState.AddModelError(key, maskError);
            }
        }

        // Routes the re-rendered EditChat form to the tab whose webhook field actually has an error,
        // so the asp-validation-for span (rendered inside a display:none .tab-pane unless the tab is
        // active) is visible. EditChat.cshtml honors ViewData["Tab"] over its default-tab heuristic;
        // both POST actions call this right before return View(model). Slack wins over Mattermost
        // when both error (arbitrary but deterministic), matching the default-tab fallback's order.
        private void SetTabFromWebhookErrors()
        {
            if (HasWebhookErrors(nameof(ChatViewModel.SlackWebhookUrl)))
                ViewData["Tab"] = "slack";
            else if (HasWebhookErrors(nameof(ChatViewModel.MattermostWebhookUrl)))
                ViewData["Tab"] = "mattermost";

            bool HasWebhookErrors(string key) =>
                ModelState.ContainsKey(key) && ModelState[key].Errors.Count > 0;
        }


        // Repopulates server-owned display state that the model-bound shell lost on a validation
        // rejection, so the re-rendered form is both correct and actionable:
        //  - Folders: ChatFoldersViewModel.DisplayFolders is get-only and populated only by the
        //    server ctor; the form never posts it. Without the rebuild, the re-render drops the
        //    folders table AND the hidden Folders.Folders[i] inputs, so the next Save (pasting the
        //    full URL as the error tells the user to) posts empty Folders and SyncFolders unbinds
        //    the chat from every managed folder. The just-picked SelectedFolders are carried over
        //    so a pending folder selection survives the round-trip.
        //  - Webhook fields: a rejected empty post binds to null, which would render an empty field
        //    with HasSlack=false — hiding the very Remove button the rejection error points to.
        //    Restore the stored mask so the error is actionable. A posted (edited/pasted) value is
        //    kept as-is so the user sees what they submitted.
        //  - Telegram title/description: display-only fields not posted by the form.
        private void RestoreStoredDisplayState(ChatViewModel model, Chat stored)
        {
            if (stored is null)
                return;

            var postedSelectedFolders = model.Folders?.SelectedFolders;
            model.Folders = BuildChatFolders(stored);

            if (postedSelectedFolders is not null)
                model.Folders.SelectedFolders = postedSelectedFolders;

            model.SlackWebhookUrl ??= WebhookUrlMasker.Mask(stored.SlackWebhookUrl);
            model.MattermostWebhookUrl ??= WebhookUrlMasker.Mask(stored.MattermostWebhookUrl);
            model.TelegramChatTitle = stored.TelegramChatTitle;
            model.TelegramChatDescription = stored.TelegramChatDescription;
        }

        private ChatFoldersViewModel BuildChatFolders(Chat chat)
        {
            var availableFolders = _folderManager.GetUserFolders(CurrentUser).Where(f => !f.Chats.Contains(chat.Id)).ToList();
            var chatFolders = _folderManager.GetValues().Where(f => chat.Folders.Contains(f.Id)).ToList();

            return new(availableFolders, chatFolders);
        }
    }
}
