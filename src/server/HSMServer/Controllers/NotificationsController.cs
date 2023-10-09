using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notification.Settings;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;


namespace HSMServer.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class NotificationsController : BaseController
    {
        private readonly ITelegramChatsManager _chatsManager;
        private readonly IFolderManager _folderManager;
        private readonly TelegramBot _telegramBot;
        private readonly TreeViewModel _tree;


        public NotificationsController(ITelegramChatsManager chatsManager, IUserManager userManager, IFolderManager folderManager,
            TreeViewModel tree, NotificationsCenter notifications) : base(userManager)
        {
            _chatsManager = chatsManager;
            _folderManager = folderManager;
            _tree = tree;

            _telegramBot = notifications.TelegramBot;
        }


        public RedirectResult OpenInvitationLink() =>
            Redirect(_telegramBot.GetInvitationLink(GetCurrentUser()));

        public async Task<RedirectResult> OpenTelegramGroup(long chatId) =>
            Redirect(await _telegramBot.GetChatLink(chatId));

        [HttpGet]
        public string CopyStartCommandForGroup(string entityId)
        {
            var id = SensorPathHelper.DecodeGuid(entityId);

            _tree.Nodes.TryGetValue(id, out var product);

            return _telegramBot.GetStartCommandForGroup(product);
        }

        public IActionResult SendTestTelegramMessage(long chatId, string entityId)
        {
            var testMessage = $"Test message for {CurrentUser.Name}.";
            if (GetEntity(entityId) is ProductNodeViewModel product)
                testMessage = $"{testMessage} (Product {product.Name})";

            _telegramBot.SendTestMessage(chatId, testMessage);

            return GetResult(entityId);
        }

        public IActionResult RemoveTelegramAuthorization(long chatId, string entityId)
        {
            _telegramBot.RemoveChat(GetEntity(entityId), chatId);

            return GetResult(entityId);
        }

        private INotificatable GetEntity(string entityId) =>
            !string.IsNullOrEmpty(entityId)
                ? _tree.Nodes[SensorPathHelper.DecodeGuid(entityId)]
                : GetCurrentUser();

        private User GetCurrentUser() => _userManager[CurrentUser.Id];

        private RedirectToActionResult GetResult(string entityId) =>
            string.IsNullOrEmpty(entityId)
                ? RedirectToAction("Index")
                : RedirectToAction(nameof(ProductController.EditProduct), ViewConstants.ProductController, new { Product = entityId });
    }
}
