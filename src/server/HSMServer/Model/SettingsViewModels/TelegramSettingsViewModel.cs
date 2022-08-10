using HSMServer.Core.Model;
using HSMServer.Core.Model.Authentication;
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

        public TelegramSettingsViewModel(User user)
        {
            EnableMessages = user.EnableTelegramMessages;
            MinStatusLevel = user.TelegramMessagesMinStatus;
            MessagesDelay = user.TelegramMessagesDelay;
        }


        internal User GetUpdatedUser(User user)
        {
            user.TelegramMessagesMinStatus = MinStatusLevel;
            user.EnableTelegramMessages = EnableMessages;
            user.TelegramMessagesDelay = MessagesDelay;

            return user;
        }
    }
}
