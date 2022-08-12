using HSMServer.Core.Model;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model
{
    public class TelegramSettingsViewModel
    {
        [Display(Name = "Enable messages")]
        public bool EnableMessages { get; set; }

        [Display(Name = "Min status level")]
        public SensorStatus MinStatusLevel { get; set; }

        [Display(Name = "Messages delay")]
        public int MessagesDelay { get; set; }


        // public constructor without parameters for action Account/UpdateTelegramSettings
        public TelegramSettingsViewModel() { }

        public TelegramSettingsViewModel(TelegramSettings settings)
        {
            EnableMessages = settings.MessagesAreEnabled;
            MinStatusLevel = settings.MessagesMinStatus;
            MessagesDelay = settings.MessagesDelay;
        }


        internal TelegramSettings ToModel() =>
            new()
            {
                MessagesMinStatus = MinStatusLevel,
                MessagesAreEnabled = EnableMessages,
                MessagesDelay = MessagesDelay,
            };
    }
}
