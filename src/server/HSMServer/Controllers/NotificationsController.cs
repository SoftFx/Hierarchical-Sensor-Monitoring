using HSMServer.Constants;
using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
using HSMServer.Helpers;
using HSMServer.Model;
using HSMServer.Notifications;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class NotificationsController : Controller
    {
        private readonly IUserManager _userManager;
        private readonly ITreeValuesCache _cache;
        private readonly TelegramBot _telegramBot;


        public NotificationsController(IUserManager userManager, ITreeValuesCache cache, INotificationsCenter notificationsCenter)
        {
            _userManager = userManager;
            _cache = cache;
            _telegramBot = notificationsCenter.TelegramBot;
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

            entity.UpdateEntity(_userManager, _cache);

            return GetResult(productId);
        }

        public RedirectResult OpenInvitationLink() =>
            Redirect(_telegramBot.GetInvitationLink(GetCurrentUser()));

        public async Task<RedirectResult> OpenTelegramGroup(long chatId) =>
            Redirect(await _telegramBot.GetChatLink(chatId));

        [HttpGet]
        public string CopyStartCommandForGroup([FromQuery(Name = "ProductId")] string encodedProductId)
        {
            var productId = SensorPathHelper.DecodeGuid(encodedProductId);
            var product = _cache.GetProduct(productId);

            return _telegramBot.GetStartCommandForGroup(product);
        }

        public IActionResult SendTestTelegramMessage(long chatId, string productId)
        {
            var testMessage = $"Test message for {(HttpContext.User as User).UserName}.";
            if (GetEntity(productId) is ProductModel product)
                testMessage = $"{testMessage} (Product {product.DisplayName})";

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
                ? _cache.GetProduct(SensorPathHelper.DecodeGuid(productId))
                : GetCurrentUser();

        private User GetCurrentUser() => _userManager.GetUser((HttpContext.User as User).Id);

        private RedirectToActionResult GetResult(string productId) =>
            string.IsNullOrEmpty(productId)
                ? RedirectToAction(nameof(Index))
                : RedirectToAction(nameof(ProductController.EditProduct), ViewConstants.ProductController, new { Product = productId });
    }
}
