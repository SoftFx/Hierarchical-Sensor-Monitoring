using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
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
        private readonly TreeViewModel _tree;


        public NotificationsController(ITelegramChatsManager chatsManager, IUserManager userManager,
            TreeViewModel tree, NotificationsCenter notifications) : base(userManager)
        {
            _chatsManager = chatsManager;
            _tree = tree;

            _telegramBot = notifications.TelegramBot;
        }


        public RedirectResult OpenInvitationLink(Guid folderId) =>
            Redirect(_chatsManager.GetInvitationLink(folderId, GetCurrentUser()));

        [HttpGet]
        public string CopyStartCommandForGroup(Guid folderId) => _chatsManager.GetGroupInvitation(folderId, GetCurrentUser());

        public async Task<RedirectResult> OpenTelegramGroup(long chatId) =>
            Redirect(await _telegramBot.GetChatLink(chatId));

        public IActionResult SendTestTelegramMessage(long chatId, string entityId)
        {
            var testMessage = $"Test message for {CurrentUser.Name}.";

            _telegramBot.SendTestMessage(chatId, testMessage);

            return GetResult(entityId);
        }

        public IActionResult RemoveTelegramAuthorization(Guid id)
        {
            _chatsManager.TryRemove(id);

            return _emptyResult;
        }

        private User GetCurrentUser() => _userManager[CurrentUser.Id];

        private RedirectToActionResult GetResult(string entityId) =>
            string.IsNullOrEmpty(entityId)
                ? RedirectToAction("Index")
                : RedirectToAction(nameof(ProductController.EditProduct), ViewConstants.ProductController, new { Product = entityId });
    }
}
