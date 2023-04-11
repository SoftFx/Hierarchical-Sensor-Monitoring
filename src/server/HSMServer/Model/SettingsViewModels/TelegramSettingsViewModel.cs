using HSMServer.Extensions;
using HSMServer.Notification.Settings;
using HSMServer.Notifications.Telegram;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text;
using SensorStatus = HSMServer.Model.TreeViewModel.SensorStatus;

namespace HSMServer.Model
{
    public class TelegramSettingsViewModel
    {
        [Display(Name = "Enable messages")]
        public bool EnableMessages { get; set; }

        [Display(Name = "Min status level")]
        public SensorStatus MinStatusLevel { get; set; } = SensorStatus.Warning;

        public string MinStatusLevelHelper { get; set; }

        [Display(Name = "Messages delay")]
        public int MessagesDelay { get; set; }

        public List<TelegramChatViewModel> Chats { get; } = new();


        // public constructor without parameters for action Account/UpdateTelegramSettings
        public TelegramSettingsViewModel() { }

        public TelegramSettingsViewModel(TelegramSettings settings)
        {
            Update(settings);
        }

        internal void Update(TelegramSettings settings)
        {
            EnableMessages = settings.MessagesAreEnabled;
            MinStatusLevel = settings.MessagesMinStatus.ToClient();
            MessagesDelay = settings.MessagesDelay;

            Chats.Clear();
            foreach (var (_, chat) in settings.Chats)
                Chats.Add(new TelegramChatViewModel(chat));
        }

        internal TelegramMessagesSettingsUpdate GetUpdateModel() =>
            new()
            {
                MinStatus = MinStatusLevel.ToCore(),
                Enabled = EnableMessages,
                Delay = MessagesDelay,
            };

        public static string GetStatusPairs(SensorStatus newStatus)
        {
            var length = Enum.GetValues<SensorStatus>().Length;

            var builder = new StringBuilder(1 << 4);

            for (int i = 0; i < length; i++)
                for (int j = 0; j < length; j++)
                    if (i != j && (i >= (int)newStatus || j >= (int)newStatus))
                        builder.Append($"{(SensorStatus)i} -> {(SensorStatus)j}, ");

            var response = builder.ToString();
            return string.IsNullOrEmpty(response) ? response : response[..^2];
        }

    }


    public class TelegramChatViewModel
    {
        public long ChatId { get; }

        public string Name { get; }

        public string AuthorizationTime { get; }


        public TelegramChatViewModel(TelegramChat chat)
        {
            ChatId = chat.Id.Identifier ?? 0L;
            Name = chat.Name?.Length == 0
                ? "Please, reinitialize account"
                : chat.Name;
            AuthorizationTime = chat.AuthorizationTime == DateTime.MinValue
                ? "-"
                : chat.AuthorizationTime.ToDefaultFormat();
        }
    }
}
