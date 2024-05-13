using HSMServer.ServerConfiguration;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Configuration
{
    public class TelegramSettingsViewModel
    {
        [Display(Name = "Bot token")]
        public string BotToken { get; set; }

        [Display(Name = "Bot name")]
        public string BotName { get; set; }

        [Display(Name = "Enable messages")]
        public bool IsEnabled { get; set; }


        public TelegramSettingsViewModel() { }

        public TelegramSettingsViewModel(IServerConfig config)
        {
            IsEnabled = config.Telegram.IsRunning;
            BotToken = config.Telegram.BotToken;
            BotName = config.Telegram.BotName;
        }
    }
}
