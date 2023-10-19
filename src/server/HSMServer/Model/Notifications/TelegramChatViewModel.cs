using HSMServer.Notifications;
using System;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Notifications
{
    public class TelegramChatViewModel
    {
        public string Name { get; }

        public string Connector { get; }

        public ConnectedChatType Type { get; }

        [Display(Name = "Authorizarion date")]
        public DateTime AuthorizationTime { get; }


        public Guid Id { get; set; }

        [Display(Name = "Messages delay")]
        public int MessagesDelay { get; set; }

        public string Description { get; set; }

        [Display(Name = "Enable messages")]
        public bool EnableMessages { get; set; }

        public ChatFoldersViewModel Folders { get; set; }


        // public constructor without parameters for action Notifications/EditChat
        public TelegramChatViewModel() { }

        public TelegramChatViewModel(TelegramChat chat, ChatFoldersViewModel folders)
        {
            Id = chat.Id;
            Name = chat.Name;
            Type = chat.Type;
            Connector = chat.Author;
            AuthorizationTime = chat.AuthorizationTime;

            Description = chat.Description;
            EnableMessages = chat.SendMessages;
            MessagesDelay = chat.MessagesAggregationTimeSec;
            Folders = folders;
        }

        internal TelegramChatUpdate ToUpdate() =>
            new()
            {
                Id = Id,
                Description = Description,
                SendMessages = EnableMessages,
                MessagesAggregationTimeSec = MessagesDelay,
            };
    }
}
