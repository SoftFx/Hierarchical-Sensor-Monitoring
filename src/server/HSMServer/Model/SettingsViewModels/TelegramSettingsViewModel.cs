using HSMServer.Core.Model;
using HSMServer.Extensions;
using System;
using System.Collections.Generic;
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

        public List<TelegramChatViewModel> Chats { get; } = new();

        public List<TelegramChatViewModel> Groups { get; } = new();


        // public constructor without parameters for action Account/UpdateTelegramSettings
        public TelegramSettingsViewModel() { }

        public TelegramSettingsViewModel(TelegramSettings settings)
        {
            EnableMessages = settings.MessagesAreEnabled;
            MinStatusLevel = settings.MessagesMinStatus;
            MessagesDelay = settings.MessagesDelay;

            foreach (var (_, chat) in settings.Chats)
            {
                var chatViewModel = new TelegramChatViewModel(chat);

                if (chat.IsGroup)
                    Groups.Add(chatViewModel);
                else
                    Chats.Add(chatViewModel);
            }
        }


        internal TelegramMessagesSettingsUpdate GetUpdateModel() =>
            new()
            {
                MinStatus = MinStatusLevel,
                Enabled = EnableMessages,
                Delay = MessagesDelay,
            };
    }


    public class TelegramChatViewModel
    {
        public long ChatId { get; }

        public string Username { get; }

        public string AuthorizationTime { get; }


        public TelegramChatViewModel(TelegramChat chat)
        {
            ChatId = chat.Id.Identifier ?? 0L;
            Username = chat.Name?.Length == 0
                ? "Please, reinitialize account"
                : chat.Name;
            AuthorizationTime = chat.AuthorizationTime == DateTime.MinValue
                ? "-"
                : chat.AuthorizationTime.ToDefaultFormat();
        }
    }
}
