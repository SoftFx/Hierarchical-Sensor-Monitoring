using HSMServer.Authentication;
using HSMServer.Constants;
using HSMServer.Core.Cache;
using HSMServer.Helpers;
using HSMServer.Model;
using HSMServer.Model.Authentication;
using HSMServer.Model.TreeViewModel;
using HSMServer.Notification.Settings;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class NotificationsController : Controller
    {
        private readonly IUserManager _userManager;
        private readonly TreeViewModel _tree;
        private readonly TelegramBot _telegramBot;


        public NotificationsController(IUserManager userManager, TreeViewModel tree, ITreeValuesCache cache, NotificationsCenter notifications)
        {
            _userManager = userManager;
            _tree = tree;

            _telegramBot = notifications.TelegramBot;
        }


        public IActionResult Index()
        {
            return View(new TelegramSettingsViewModel((HttpContext.User as User).Notifications.Telegram));
        }

        [HttpPost]
        public IActionResult UpdateTelegramSettings(TelegramSettingsViewModel telegramSettings, string productId)
        {
            var entity = GetEntity(productId);
            entity.Notifications.Telegram.Update(telegramSettings.GetUpdateModel());

            entity.UpdateEntity(_userManager, _tree);

            return GetResult(productId);
        }

        [HttpGet]
        public string UpdateViewStatusLevelHelper([FromQuery] SensorStatus newSensorStatus) =>
            TelegramSettingsViewModel.GetStatusPairs(newSensorStatus);

        public RedirectResult OpenInvitationLink() =>
            Redirect(_telegramBot.GetInvitationLink(GetCurrentUser()));

        public async Task<RedirectResult> OpenTelegramGroup(long chatId) =>
            Redirect(await _telegramBot.GetChatLink(chatId));

        [HttpGet]
        public string CopyStartCommandForGroup([FromQuery(Name = "ProductId")] string encodedProductId)
        {
            var productId = SensorPathHelper.DecodeGuid(encodedProductId);

            _tree.Nodes.TryGetValue(productId, out var product);

            return _telegramBot.GetStartCommandForGroup(product);
        }

        public IActionResult SendTestTelegramMessage(long chatId, string productId)
        {
            var testMessage = $"Test message for {(HttpContext.User as User).Name}.";
            if (GetEntity(productId) is ProductNodeViewModel product)
                testMessage = $"{testMessage} (Product {product.Name})";

            _telegramBot.SendTestMessage(chatId, testMessage);

            return GetResult(productId);
        }

        public IActionResult RemoveTelegramAuthorization(long chatId, string productId)
        {
            _telegramBot.RemoveChat(GetEntity(productId), chatId);

            return GetResult(productId);
        }

        private INotificatable GetEntity(string productId) =>
            !string.IsNullOrEmpty(productId)
                ? _tree.Nodes[SensorPathHelper.DecodeGuid(productId)]
                : GetCurrentUser();

        private User GetCurrentUser() => _userManager[(HttpContext.User as User).Id];

        private RedirectToActionResult GetResult(string productId) =>
            string.IsNullOrEmpty(productId)
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(ProductController.EditProduct), ViewConstants.ProductController, new { Product = productId });
    }
}
