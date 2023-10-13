using HSMServer.Authentication;
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
