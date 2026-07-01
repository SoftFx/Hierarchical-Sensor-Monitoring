using HSMServer.Authentication;
using HSMServer.Notifications;
using System;
using System.ComponentModel.DataAnnotations;

namespace HSMServer.Model.Notifications
{
    public class SlackDestinationViewModel
    {
        public Guid Id { get; set; }

        [Required(ErrorMessage = "Name is required")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Webhook URL is required")]
        [Url(ErrorMessage = "Webhook URL must be a valid URL")]
        public string WebhookUrl { get; set; }

        public string Description { get; set; }

        public string Author { get; set; }

        public DateTime CreationDate { get; set; }

        [Display(Name = "Messages delay")]
        public int MessagesDelay { get; set; } = 60;

        [Display(Name = "Enable messages")]
        public bool EnableMessages { get; set; }

        public ChatFoldersViewModel Folders { get; set; } = new();


        public SlackDestinationViewModel() { }

        public SlackDestinationViewModel(SlackDestination destination, IUserManager userManager = null, ChatFoldersViewModel folders = null)
        {
            Id = destination.Id;
            Name = destination.Name;
            WebhookUrl = destination.WebhookUrl;
            Description = destination.Description;
            Author = ResolveAuthorName(destination.AuthorId, userManager);
            CreationDate = destination.CreationDate;
            MessagesDelay = destination.MessagesAggregationTimeSec;
            EnableMessages = destination.SendMessages;
            Folders = folders ?? new ChatFoldersViewModel();
        }


        internal SlackAddRequest ToAddRequest(Guid authorId) =>
            new()
            {
                AuthorId = authorId,
                Name = Name,
                Description = Description,
                WebhookUrl = WebhookUrl,
                MessagesAggregationTimeSec = MessagesDelay,
            };

        internal SlackDestinationUpdate ToUpdate() =>
            new()
            {
                Id = Id,
                Name = Name,
                Description = Description,
                WebhookUrl = WebhookUrl,
                SendMessages = EnableMessages,
                MessagesAggregationTimeSec = MessagesDelay,
            };


        private static string ResolveAuthorName(Guid? authorId, IUserManager userManager) =>
            authorId is not null && userManager is not null && userManager.TryGetValueById(authorId, out var user)
                ? user.Name
                : authorId?.ToString();
    }
}
