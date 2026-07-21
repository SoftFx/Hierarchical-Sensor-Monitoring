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

        internal IChatsManager ChatsManager { get; }


        public NotificationsController(IChatsManager chatsManager, NotificationsCenter notifications,
            IFolderManager folderManager, IUserManager userManager) : base(userManager)
        {
            ChatsManager = chatsManager;
            _folderManager = folderManager;
            _notifications = notifications;
            _telegramBot = notifications.TelegramBot;
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
            if (!ModelState.IsValid)
                return View(model);

            if (await ChatsManager.TryUpdate(model.ToUpdate()))
                await SyncFolders(model);

            return ChatsManager.TryGetValue(model.Id, out var chat)
                ? View(new ChatViewModel(chat, BuildChatFolders(chat)))
                : RedirectToAction(nameof(ProductController.Index), ViewConstants.ProductController);
        }

        [HttpGet]
        [AuthorizeIsAdmin]
        public IActionResult Index() => View(nameof(Index), new ChatsSettingsViewModel(ChatsManager, _folderManager));

        [HttpGet]
        [AuthorizeIsAdmin]
        public IActionResult AddChat() => View(nameof(EditChat), new ChatViewModel { EnableMessages = true });

        [HttpPost]
        [AuthorizeIsAdmin]
        public async Task<IActionResult> AddChat(ChatViewModel model)
        {
            if (!ModelState.IsValid)
                return View(nameof(EditChat), model);

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
