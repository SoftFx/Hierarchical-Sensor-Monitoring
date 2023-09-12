using HSMServer.Notification.Settings;
using HSMServer.Notifications;
using System;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model
{
    public class TelegramSettingsViewModel
    {
        public Guid Id { get; set; }

        [Display(Name = "Enable messages")]
        public bool EnableMessages { get; set; }

        [Display(Name = "Messages delay")]
        public int MessagesDelay { get; set; }


        // public constructor without parameters for action Account/UpdateTelegramSettings
        public TelegramSettingsViewModel() { }

        public TelegramSettingsViewModel(TelegramChat settings)
        {
            Id = settings.Id;
            EnableMessages = settings.SendMessages;
            MessagesDelay = settings.MessagesAggregationTimeSec;
        }

        internal TelegramMessagesSettingsUpdate GetUpdateModel() =>
            new()
            {
                Enabled = EnableMessages,
                Delay = MessagesDelay,
            };
    }
}
