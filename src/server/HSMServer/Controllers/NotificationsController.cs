using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Filters.FolderRoleFilters;
using HSMServer.Filters.TelegramRoleFilters;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
using HSMServer.Model.Folders;
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
        public IActionResult EditChat(Guid id) => ChatsManager.TryGetValue(id, out var chat)
            ? View(new ChatViewModel(chat, BuildChatFolders(chat)))
            : _emptyResult;

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
        public IActionResult AddChat() => View(nameof(EditChat), new ChatViewModel { EnableMessages = true });

        [HttpPost]
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

            return RedirectToAction(nameof(ConfigurationController.Index), ViewConstants.ConfigurationController);
        }

        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task RemoveChat(Guid id) => await ChatsManager.TryRemove(new(id, CurrentInitiator));

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

        [HttpPost]
        public async Task<IActionResult> SendTestSlackMessage([FromQuery] Guid id)
        {
            if (ChatsManager.TryGetValue(id, out var chat))
                await _notifications.SlackChannel.SendTestAsync(chat);

            return Ok();
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

            var removedFolders = updated.Folders.Except(model.Folders.Folders).ToList();

            foreach (var folderId in model.Folders.SelectedFolders)
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
