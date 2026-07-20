using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Extensions;
using HSMServer.Model.Folders.ViewModels;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using System;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Notifications
{
    public class ChatViewModel
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "Name is required")]
        [StringLength(100, ErrorMessage = "{0} length should be less than {1}.")]
        public string Name { get; set; }

        public string Description { get; set; }

        public string Author { get; set; }

        public DateTime CreationDate { get; set; }


        [Display(Name = "Authorization date")]
        public DateTime? AuthorizationTime { get; set; }

        public long? TelegramChatId { get; set; }

        public ConnectedChatType? TelegramType { get; set; }

        public string TelegramChatTitle { get; set; }

        public string TelegramChatDescription { get; set; }


        [Url(ErrorMessage = "Slack webhook URL must be a valid URL")]
        public string SlackWebhookUrl { get; set; }

        [Url(ErrorMessage = "Mattermost webhook URL must be a valid URL")]
        public string MattermostWebhookUrl { get; set; }


        [Display(Name = "Messages delay")]
        public int MessagesDelay { get; set; } = 60;

        [Display(Name = "Enable messages")]
        public bool EnableMessages { get; set; } = true;


        public ChatFoldersViewModel Folders { get; set; } = new();


        public bool HasTelegram => TelegramChatId is not null;

        public bool HasSlack => !string.IsNullOrEmpty(SlackWebhookUrl);

        public bool HasMattermost => !string.IsNullOrEmpty(MattermostWebhookUrl);


        public string ChatBrandIcons()
        {
            var icons = string.Empty;

            if (HasTelegram)
                icons += $"<i class='{ChatIcons.TelegramBrandClass}'></i> ";
            if (HasSlack)
                icons += $"<i class='{ChatIcons.SlackBrandClass}'></i> ";
            if (HasMattermost)
                icons += $"{ChatIcons.MattermostBrandIconSvg} ";

            return string.IsNullOrEmpty(icons) ? null : icons.TrimEnd();
        }


        public ChatViewModel() { }

        public ChatViewModel(Chat chat, ChatFoldersViewModel folders)
        {
            Id = chat.Id;
            Name = chat.Name;
            Description = chat.Description;
            Author = chat.Author;
            CreationDate = chat.CreationDate;
            AuthorizationTime = chat.AuthorizationTime;
            TelegramChatId = chat.TelegramChatId?.Identifier;
            TelegramType = chat.TelegramType;
            TelegramChatTitle = chat.TelegramChatTitle;
            TelegramChatDescription = chat.TelegramChatDescription;
            SlackWebhookUrl = chat.SlackWebhookUrl;
            MattermostWebhookUrl = chat.MattermostWebhookUrl;
            MessagesDelay = chat.MessagesAggregationTimeSec;
            EnableMessages = chat.SendMessages;
            Folders = folders ?? new ChatFoldersViewModel();
        }


        internal ChatUpdate ToUpdate() =>
            new()
            {
                Id = Id,
                Name = Name,
                Description = Description,
                SendMessages = EnableMessages,
                MessagesAggregationTimeSec = MessagesDelay,
                SlackWebhookUrl = SlackWebhookUrl,
                MattermostWebhookUrl = MattermostWebhookUrl,
            };

        internal Chat ToNewChat(Guid authorId)
        {
            var entity = new ChatEntity
            {
                Id = (Id == Guid.Empty ? Guid.NewGuid() : Id).ToByteArray(),
                Author = authorId.ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = Name,
                Description = Description ?? string.Empty,
                SendMessages = EnableMessages,
                MessagesAggregationTimeSec = MessagesDelay,
                SlackWebhookUrl = SlackWebhookUrl,
                MattermostWebhookUrl = MattermostWebhookUrl,
            };

            return new Chat(entity);
        }
    }
}
