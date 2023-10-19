using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Folders;
using HSMServer.Model.Folders;
using HSMServer.Model.Notifications;
using HSMServer.Notifications;
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
        private readonly ITelegramChatsManager _chatsManager;
        private readonly IFolderManager _folderManager;
        private readonly TelegramBot _telegramBot;


        public NotificationsController(ITelegramChatsManager chatsManager, NotificationsCenter notifications,
            IFolderManager folderManager, IUserManager userManager) : base(userManager)
        {
            _chatsManager = chatsManager;
            _folderManager = folderManager;
            _telegramBot = notifications.TelegramBot;
        }


        [HttpGet]
        public IActionResult EditChat(Guid id) => _chatsManager.TryGetValue(id, out var chat)
            ? View(new TelegramChatViewModel(chat, BuildChatFolders(chat)))
            : _emptyResult;

        public async Task<IActionResult> EditChat(TelegramChatViewModel updateModel)
        {
            if (await _chatsManager.TryUpdate(updateModel.ToUpdate()))
            {
                var updatedChat = _chatsManager[updateModel.Id];
                var removedFolders = updatedChat.Folders.Except(updateModel.Folders.Folders).ToList();

                foreach (var folderId in updateModel.Folders.SelectedFolders)
                    if (_folderManager.TryGetValue(folderId, out var folder))
                        await UpdateFolder(folderId, new HashSet<Guid>(folder.TelegramChats) { updateModel.Id });

                foreach (var folderId in removedFolders)
                    if (_folderManager.TryGetValue(folderId, out var folder))
                    {
                        var folderChats = new HashSet<Guid>(folder.TelegramChats);
                        folderChats.Remove(updateModel.Id);

                        await UpdateFolder(folderId, folderChats);
                    }
            }

            return _chatsManager.TryGetValue(updateModel.Id, out var chat)
                ? View(new TelegramChatViewModel(chat, BuildChatFolders(chat)))
                : RedirectToAction(nameof(ProductController.Index), ViewConstants.ProductController);
        }

        public async Task RemoveChat(Guid id) => await _chatsManager.TryRemove(new(id));

        public RedirectResult OpenInvitationLink(Guid folderId) =>
            Redirect(_chatsManager.GetInvitationLink(folderId, CurrentUser));

        [HttpGet]
        public string GetGroupInvitation(Guid folderId) => _chatsManager.GetGroupInvitation(folderId, CurrentUser);

        public async Task<RedirectResult> OpenTelegramGroup(long chatId) =>
            Redirect(await _telegramBot.GetChatLink(chatId));

        public void SendTestTelegramMessage(long chatId)
        {
            var testMessage = $"Test message for {CurrentUser.Name}.";

            _telegramBot.SendTestMessage(chatId, testMessage);
        }


        private ChatFoldersViewModel BuildChatFolders(TelegramChat chat)
        {
            var availableFolders = _folderManager.GetUserFolders(CurrentUser).Where(f => !f.TelegramChats.Contains(chat.Id)).ToList();
            var chatFolders = _folderManager.GetValues().Where(f => chat.Folders.Contains(f.Id)).ToList();

            return new(availableFolders, chatFolders);
        }

        private async Task UpdateFolder(Guid folderId, HashSet<Guid> folderChats)
        {
            var update = new FolderUpdate()
            {
                Id = folderId,
                TelegramChats = folderChats,
                Initiator = CurrentInitiator,
            };

            await _folderManager.TryUpdate(update);
        }
    }
}
