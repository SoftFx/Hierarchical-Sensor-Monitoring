using HSMServer.Authentication;
using HSMServer.Model.Notifications;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;


namespace HSMServer.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class NotificationsController : BaseController
    {
        private readonly ITelegramChatsManager _chatsManager;
        private readonly TelegramBot _telegramBot;


        public NotificationsController(ITelegramChatsManager chatsManager, IUserManager userManager, NotificationsCenter notifications)
            : base(userManager)
        {
            _chatsManager = chatsManager;
            _telegramBot = notifications.TelegramBot;
        }


        [HttpGet]
        public IActionResult EditChat(Guid id) => _chatsManager.TryGetValue(id, out var chat)
            ? View(new TelegramChatViewModel(chat))
            : _emptyResult;

        public async Task<IActionResult> EditChat(TelegramChatViewModel chat)
        {
            await _chatsManager.TryUpdate(chat.ToUpdate());

            return View(new TelegramChatViewModel(_chatsManager[chat.Id]));
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
    }
}
