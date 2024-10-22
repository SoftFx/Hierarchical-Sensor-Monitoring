using HSMServer.ServerConfiguration;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Configuration
{
    public class TelegramSettingsViewModel
    {
        private const string Stopped = "Stopped"; 
        private const string Running = "Running"; 


        [Display(Name = "Bot token")]
        public string BotToken { get; set; }

        [Display(Name = "Bot name")]
        public string BotName { get; set; }

        [Display(Name = "Enable messages")]
        public bool IsEnabled { get; set; }
        
        [Display(Name = "Current Bot Status")]
        public string Status { get; set; }


        public TelegramSettingsViewModel() { }

        public TelegramSettingsViewModel(IServerConfig config, bool isBotRunning)
        {
            IsEnabled = config.Telegram.IsRunning;
            BotToken = config.Telegram.BotToken;
            BotName = config.Telegram.BotName;
            Status = isBotRunning ? Running : Stopped;
        }
    }
}
