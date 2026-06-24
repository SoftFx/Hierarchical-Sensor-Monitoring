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

        [Display(Name = "Enable messages")]
        public bool EnableMessages { get; set; }


        public SlackDestinationViewModel() { }

        public SlackDestinationViewModel(SlackDestination destination)
        {
            Id = destination.Id;
            Name = destination.Name;
            WebhookUrl = destination.WebhookUrl;
            Description = destination.Description;
            Author = destination.AuthorId?.ToString();
            CreationDate = destination.CreationDate;
            EnableMessages = destination.SendMessages;
        }


        internal SlackAddRequest ToAddRequest(Guid authorId) =>
            new()
            {
                AuthorId = authorId,
                Name = Name,
                Description = Description,
                WebhookUrl = WebhookUrl,
            };

        internal SlackDestinationUpdate ToUpdate() =>
            new()
            {
                Id = Id,
                Name = Name,
                Description = Description,
                WebhookUrl = WebhookUrl,
                SendMessages = EnableMessages,
            };
    }
}
