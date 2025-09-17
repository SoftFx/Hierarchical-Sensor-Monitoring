using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Filters.FolderRoleFilters;
using HSMServer.Filters.TelegramRoleFilters;
using HSMServer.Folders;
using HSMServer.Model.Authentication;
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
        private readonly IFolderManager _folderManager;
        private readonly TelegramBot _telegramBot;

        internal ITelegramChatsManager ChatsManager { get; }


        public NotificationsController(ITelegramChatsManager chatsManager, NotificationsCenter notifications,
            IFolderManager folderManager, IUserManager userManager) : base(userManager)
        {
            ChatsManager = chatsManager;
            _folderManager = folderManager;
            _telegramBot = notifications.TelegramBot;
        }


        [HttpGet]
        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public IActionResult EditChat(Guid id) => ChatsManager.TryGetValue(id, out var chat)
            ? View(new TelegramChatViewModel(chat, BuildChatFolders(chat)))
            : _emptyResult;

        [HttpPost]
        [TelegramRoleFilterByEditModel(nameof(updateModel), ProductRoleEnum.ProductManager)]
        public async Task<IActionResult> EditChat(TelegramChatViewModel updateModel)
        {
            if (await ChatsManager.TryUpdate(updateModel.ToUpdate()))
            {
                var updatedChat = ChatsManager[updateModel.Id];
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

            return ChatsManager.TryGetValue(updateModel.Id, out var chat)
                ? View(new TelegramChatViewModel(chat, BuildChatFolders(chat)))
                : RedirectToAction(nameof(ProductController.Index), ViewConstants.ProductController);
        }

        [TelegramRoleFilterById(nameof(id), ProductRoleEnum.ProductManager)]
        public async Task RemoveChat(Guid id) => await ChatsManager.TryRemove(new(id));

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
