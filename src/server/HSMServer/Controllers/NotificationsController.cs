using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Folders;
using HSMServer.Helpers;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notification.Settings;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Threading.Tasks;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class NotificationsController : BaseController
    {
        private readonly IFolderManager _folderManager;
        private readonly TelegramBot _telegramBot;
        private readonly TreeViewModel _tree;


        public NotificationsController(IUserManager userManager, IFolderManager folderManager,
            TreeViewModel tree, NotificationsCenter notifications) : base(userManager)
        {
            _folderManager = folderManager;
            _tree = tree;

            _telegramBot = notifications.TelegramBot;
        }


        public IActionResult Index()
        {
            return View(new TelegramSettingsViewModel(CurrentUser.Notifications.UsedTelegram, CurrentUser.Id));
        }

        [HttpPost]
        public IActionResult ChangeInheritance(string productId, bool fromParent)
        {
            var update = new TelegramMessagesSettingsUpdate()
            {
                Inheritance = fromParent ? InheritedSettings.FromParent : InheritedSettings.Custom
            };

            return UpdateTelegramMessageSettings(productId, update);
        }

        [HttpPost]
        public void ChangeAutoSubscription(string productId, bool autoSubscription)
        {
            if (_tree.Nodes.TryGetValue(SensorPathHelper.DecodeGuid(productId), out var product))
            {
                product.Notifications.AutoSubscription = autoSubscription;

                _tree.UpdateProductNotificationSettings(product);
            }
        }

        [HttpPost]
        public IActionResult UpdateTelegramSettings(TelegramSettingsViewModel telegramSettings, string entityId)
        {
            var update = telegramSettings.GetUpdateModel();

            if (!string.IsNullOrEmpty(entityId) && Guid.TryParse(entityId, out var id) &&
                _folderManager.TryGetValue(id, out var folder))
            {
                folder.Notifications.Telegram.Update(update);

                _folderManager.TryUpdate(folder);

                return PartialView("_MessagesSettings", new TelegramSettingsViewModel(folder.Notifications.Telegram, folder.Id));
            }

            return UpdateTelegramMessageSettings(entityId, update);
        }

        [HttpGet]
        public string UpdateViewStatusLevelHelper([FromQuery] SensorStatus newSensorStatus) =>
            TelegramSettingsViewModel.GetStatusPairs(newSensorStatus);

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

        private IActionResult UpdateTelegramMessageSettings(string productId, TelegramMessagesSettingsUpdate update)
        {
            var entity = GetEntity(productId);
            entity.Notifications.Telegram.Update(update);

            entity.UpdateEntity(_userManager, _tree);

            return GetResult(productId);
        }

        private INotificatable GetEntity(string entityId) =>
            !string.IsNullOrEmpty(entityId)
                ? _tree.Nodes[SensorPathHelper.DecodeGuid(entityId)]
                : GetCurrentUser();

        private User GetCurrentUser() => _userManager[CurrentUser.Id];

        private RedirectToActionResult GetResult(string entityId) =>
            string.IsNullOrEmpty(entityId)
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(ProductController.EditProduct), ViewConstants.ProductController, new { Product = entityId });
    }
}
