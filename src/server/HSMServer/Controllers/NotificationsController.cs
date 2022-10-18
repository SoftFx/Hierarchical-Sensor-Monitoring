using HSMServer.Core.Authentication;
using HSMServer.Core.Model.Authentication;
using HSMServer.Core.Notifications;
using HSMServer.Model;
using Microsoft.AspNetCore.Mvc;
using System.Threading.Tasks;

namespace HSMServer.Controllers
{
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class NotificationsController : Controller
    {
        private readonly IUserManager _userManager;
        private readonly TelegramBot _telegramBot;


        public NotificationsController(IUserManager userManager, INotificationsCenter notificationsCenter)
        {
            _userManager = userManager;
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
            Redirect(_telegramBot.GetInvitationLink(HttpContext.User as User));

        public async Task<RedirectResult> OpenTelegramGroup(long chatId) =>
            Redirect(await _telegramBot.GetChatLink(chatId));

        [HttpGet]
        public string CopyStartCommandForGroup() =>
            _telegramBot.GetStartCommandForGroup(HttpContext.User as User);

        public IActionResult SendTestTelegramMessage(long chatId)
        {
            _telegramBot.SendTestMessage(chatId, $"Test message for {(HttpContext.User as User).UserName}");

            return RedirectToAction(nameof(Index));
        }

        public IActionResult RemoveTelegramAuthorization(long chatId)
        {
            _telegramBot.RemoveChat(HttpContext.User as User, chatId);

            return RedirectToAction(nameof(Index));
        }
    }
}
