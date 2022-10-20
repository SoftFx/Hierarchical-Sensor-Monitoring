using HSMServer.Core.Authentication;
using HSMServer.Core.Cache;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Notifications;
using HSMServer.Helpers;
using HSMServer.Model;
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
        public IActionResult UpdateTelegramSettings(TelegramSettingsViewModel telegramSettings)
        {
            var user = _userManager.GetCopyUser((HttpContext.User as User).Id);
            user.Notifications.Telegram.Update(telegramSettings.GetUpdateModel());

            _userManager.UpdateUser(user);

            return RedirectToAction(nameof(Index));
        }

        public RedirectResult OpenInvitationLink() =>
            Redirect(_telegramBot.GetInvitationLink(GetCurrentUser()));

        public async Task<RedirectResult> OpenTelegramGroup(long chatId) =>
            Redirect(await _telegramBot.GetChatLink(chatId));

        [HttpGet]
        public string CopyStartCommandForGroup([FromQuery(Name = "ProductId")] string encodedProductId)
        {
            var productId = SensorPathHelper.Decode(encodedProductId);
            var product = _cache.GetProduct(productId);

            return _telegramBot.GetStartCommandForGroup(product);
        }

        public IActionResult SendTestTelegramMessage(long chatId)
        {
            _telegramBot.SendTestMessage(chatId, $"Test message for {(HttpContext.User as User).UserName}");

            return RedirectToAction(nameof(Index));
        }

        public IActionResult RemoveTelegramAuthorization(long chatId, string productId)
        {
            INotificatable entity = !string.IsNullOrEmpty(productId)
                ? _cache.GetProduct(productId)
                : GetCurrentUser();

            _telegramBot.RemoveChat(entity, chatId);

            return RedirectToAction(nameof(Index));
        }

        private User GetCurrentUser() => _userManager.GetUser((HttpContext.User as User).Id);
    }
}
