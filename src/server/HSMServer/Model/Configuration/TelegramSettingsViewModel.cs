using HSMServer.ServerConfiguration;

namespace HSMServer.Model.Configuration
{
    public class TelegramSettingsViewModel
    {
        public string BotToken { get; set; }

        public string BotName { get; set; }

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
