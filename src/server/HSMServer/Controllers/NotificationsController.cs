using HSMServer.Attributes;
using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Filters.FolderRoleFilters;
using HSMServer.Filters.TelegramRoleFilters;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
using HSMServer.Model.Configuration;
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

        internal IChatsManager ChatsManager { get; }


        public NotificationsController(IChatsManager chatsManager, NotificationsCenter notifications,
            IFolderManager folderManager, IUserManager userManager,
            ChatSensorUsageCalculator usageCalculator) : base(userManager)
        {
            ChatsManager = chatsManager;
            _folderManager = folderManager;
            _notifications = notifications;
            _telegramBot = notifications.TelegramBot;
            _usageCalculator = usageCalculator;
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

            if (!ModelState.IsValid)
            {
                // Re-render must carry the server-owned folder data, not the model-bound shell.
                // ChatFoldersViewModel.DisplayFolders is get-only and populated only by the server
                // ctor; the form never posts it, so the re-render would otherwise drop the folders
                // table AND the hidden Folders.Folders[i] inputs. The next Save (the user pasting the
                // full URL as the error tells them to) would then post empty Folders, and SyncFolders
                // would unbind the chat from every managed folder (#1329 review).
                if (stored is not null)
                    model.Folders = BuildChatFolders(stored);

                // Land the user on the tab whose field actually has the error. EditChat.cshtml
                // renders asp-validation-for spans inside .tab-pane fade divs that are display:none
                // unless that tab is active; without this the default-tab heuristic (telegram → slack
                // → mattermost) hides the rejection on most chat configurations — e.g. a Telegram-
                // bound chat with a Slack webhook lands on the telegram tab and the Slack error is
                // invisible, recreating exactly the silent-rotation failure the guard exists to fix.
                SetTabFromWebhookErrors();

                return View(model);
            }

            if (await ChatsManager.TryUpdate(model.ToUpdate()))
                await SyncFolders(model);

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

            if (!ModelState.IsValid)
            {
                // Same folder-rebuild as EditChat POST — see there for why the server-owned folder
                // data must be repopulated before re-render or the next Save unbinds folders.
                if (stored is not null)
                    model.Folders = BuildChatFolders(stored);

                // And the same tab-routing — see EditChat POST for why the failing field's tab must
                // become active or the rejection error is rendered into a display:none pane.
                SetTabFromWebhookErrors();

                return View(nameof(EditChat), model);
            }

            // The user may have already triggered /start against the pre-allocated guid, in which
            // case the Chat row exists in storage with a Telegram binding but no admin-set name.
            // TryAdd would refuse (id collision) and silently lose the Name the user just typed.
            // Detect that case and switch to an update path so the form Save is idempotent w.r.t.
            // the order of "fill name" vs "click setup help".
            if (ChatsManager.TryGetValue(model.Id, out _))
            {
                if (await ChatsManager.TryUpdate(model.ToUpdate()))
                    await SyncFolders(model);

                return RedirectToAction(nameof(Index), ViewConstants.NotificationsController);
            }

            var chat = model.ToNewChat(CurrentUser.Id);

            if (await ChatsManager.TryAdd(chat))
            {
                model.Id = chat.Id;
                await SyncFolders(model);
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
            await ClearChannel(id, clearTelegram: true);

        [HttpPost]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> ClearSlackWebhook(Guid id) =>
            await ClearChannel(id, clearSlack: true);

        [HttpPost]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> ClearMattermostWebhook(Guid id) =>
            await ClearChannel(id, clearMattermost: true);


        private async Task<IActionResult> ClearChannel(Guid id, bool clearTelegram = false, bool clearSlack = false, bool clearMattermost = false)
        {
            var update = new ChatUpdate
            {
                Id = id,
                ClearTelegramBinding = clearTelegram,
                ClearSlackWebhook = clearSlack,
                ClearMattermostWebhook = clearMattermost,
            };

            return await ChatsManager.TryUpdate(update) ? Ok() : NotFound();
        }


        // Webhook URL validation moved server-side (was [Url] on ChatViewModel — the masked display
        // value `https://…/••••` fails Uri.TryCreate, so the attribute was removed). The masked
        // sentinel must match Mask(stored) exactly, or the post is rejected: the field is a plain
        // editable input pre-filled with the mask, so an admin who partially edits it (e.g. changes
        // only the host) must be told — otherwise ToUpdate would silently drop the edit because
        // IsMasked is a substring test (#1329 review: a rotation could appear to succeed while
        // nothing changed). The classification lives in WebhookUrlMasker.ValidatePosted so it's unit-
        // covered; a real pasted URL still has to pass the absolute http/https check here.
        //
        // Empty-on-stored regression guard: pre-PR, ToUpdate passed the posted value straight
        // through, so Chat.ApplyUpdate (`update.SlackWebhookUrl ?? SlackWebhookUrl`) received "" and
        // the webhook was cleared. Now ResolveWebhook maps empty → null = "no change", so deleting
        // the field contents became a silent no-op — the admin sees a success redirect while the
        // old webhook stays live (#1329 review). The sanctioned path is Remove Slack / Remove
        // Mattermost (ClearSlackWebhook/ClearMattermostWebhook flags); reject empty and point there.
        // No stored webhook → empty is valid (e.g. brand-new chat, or clearing an already-cleared
        // field), so the guard only fires when stored has a value.
        private void ValidateWebhooks(ChatViewModel model, Chat stored)
        {
            ValidateWebhook(model.SlackWebhookUrl, stored?.SlackWebhookUrl, nameof(ChatViewModel.SlackWebhookUrl), "Slack");
            ValidateWebhook(model.MattermostWebhookUrl, stored?.MattermostWebhookUrl, nameof(ChatViewModel.MattermostWebhookUrl), "Mattermost");

            void ValidateWebhook(string posted, string storedUrl, string key, string channelName)
            {
                // MVC binds an empty text input to null via ConvertEmptyStringToNull, so both null
                // and "" reach here. Empty + stored webhook = the regression guard (point to Remove).
                // Empty + no stored webhook is legitimate (new chat, or an already-cleared channel) —
                // MUST return here, not fall through: Uri.TryCreate(null, Absolute) returns false,
                // which would add a spurious "must be a valid URL" error to a field the user never
                // filled in, breaking AddChat for any chat missing one of the two webhooks (#1329
                // review).
                if (string.IsNullOrWhiteSpace(posted))
                {
                    if (!string.IsNullOrEmpty(storedUrl))
                        ModelState.AddModelError(key, $"Use Remove {channelName} to delete the webhook.");

                    return;
                }

                var maskError = WebhookUrlMasker.ValidatePosted(posted, storedUrl);
                if (maskError != null)
                {
                    ModelState.AddModelError(key, maskError);
                    return;
                }

                // ValidatePosted returned null: the only remaining case is a real URL. Masked values
                // can't reach here (IsMasked + matching Mask(stored) returned null from ValidatePosted;
                // IsMasked + mismatch returned an error). Real URLs must be well-formed absolute
                // http/https.
                if (WebhookUrlMasker.IsMasked(posted))
                    return;

                if (!Uri.TryCreate(posted, UriKind.Absolute, out var uri) ||
                    (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                    ModelState.AddModelError(key, "Webhook URL must be a valid URL");
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


        private ChatFoldersViewModel BuildChatFolders(Chat chat)
        {
            var availableFolders = _folderManager.GetUserFolders(CurrentUser).Where(f => !f.Chats.Contains(chat.Id)).ToList();
            var chatFolders = _folderManager.GetValues().Where(f => chat.Folders.Contains(f.Id)).ToList();

            return new(availableFolders, chatFolders);
        }

        private async Task SyncFolders(ChatViewModel model)
        {
            if (!ChatsManager.TryGetValue(model.Id, out var updated))
                return;

            // SelectedFolders and the implicit "removed" set are attacker-controlled POST data.
            // EditChat's role filter only guarantees the user manages *some* folder this chat is
            // bound to — it does not authorise mutating other folders. Re-check membership here.
            var managedFolderIds = _folderManager.GetUserFolders(CurrentUser).Select(f => f.Id).ToHashSet();

            var removedFolders = updated.Folders
                .Except(model.Folders.Folders)
                .Where(managedFolderIds.Contains)
                .ToList();

            foreach (var folderId in model.Folders.SelectedFolders.Where(managedFolderIds.Contains))
                if (_folderManager.TryGetValue(folderId, out var folder))
                    await UpdateFolder(folderId, new HashSet<Guid>(folder.Chats) { model.Id });

            foreach (var folderId in removedFolders)
                if (_folderManager.TryGetValue(folderId, out var folder))
                {
                    var folderChats = new HashSet<Guid>(folder.Chats);
                    folderChats.Remove(model.Id);

                    await UpdateFolder(folderId, folderChats);
                }
        }

        private async Task UpdateFolder(Guid folderId, HashSet<Guid> folderChats)
        {
            var update = new FolderUpdate()
            {
                Id = folderId,
                Chats = folderChats,
                Initiator = CurrentInitiator,
            };

            await _folderManager.TryUpdate(update);
        }
    }
}
