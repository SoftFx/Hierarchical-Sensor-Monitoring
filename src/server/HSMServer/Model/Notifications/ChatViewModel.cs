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

        // True when the form is rendering for a brand-new chat (AddChat flow). Decoupled from
        // `Id == Guid.Empty` because AddChat pre-allocates a guid up-front so the Telegram
        // bot-invite path can build an invitation token against it before any row is in storage.
        public bool IsNewChat { get; set; }

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


        // Bound to the EditChat input. On GET this carries the masked display value
        // (WebhookUrlMasker.Mask) — the raw secret is never rendered. On POST it carries either the
        // unchanged masked sentinel (→ ToUpdate emits null, "no change") or a freshly-pasted URL.
        // URL-shape validation moved server-side: the masked value (with non-ASCII bullets) fails
        // [Url], so the attribute was removed and the controller runs Uri.TryCreate on non-masked
        // values, adding ModelState errors for genuinely malformed input.
        public string SlackWebhookUrl { get; set; }

        public string MattermostWebhookUrl { get; set; }


        [Display(Name = "Messages delay")]
        public int MessagesDelay { get; set; } = 60;

        [Display(Name = "Enable messages")]
        public bool EnableMessages { get; set; } = true;


        public int SensorUsageCount { get; set; }

        // True when Compute() skipped at least one sensor due to a concurrent cache mutation —
        // the count is a lower bound, not an authoritative total. Rendered as "≥N sensors".
        public bool SensorUsageIncomplete { get; set; }

        public string SensorUsageBadgeText
        {
            get
            {
                var prefix = SensorUsageIncomplete ? "≥" : string.Empty;
                var noun = SensorUsageCount == 1 ? "sensor" : "sensors";
                // InvariantCulture so the group separator is stable (1,247) regardless of the
                // server's ambient culture — the badge text is asserted in tests and docs.
                return $"{prefix}{SensorUsageCount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} {noun}";
            }
        }

        public string SensorUsageBadgeTitle => SensorUsageIncomplete
            ? "Number of sensors whose alerts would be delivered to this chat. Count may be incomplete — at least one sensor was skipped during the scan."
            : "Number of sensors whose alerts would be delivered to this chat";


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
            // Mask on read — the raw webhook secret never reaches the view. ToUpdate treats the
            // masked sentinel as "no change" on POST, so the stored cleartext URL survives.
            SlackWebhookUrl = WebhookUrlMasker.Mask(chat.SlackWebhookUrl);
            MattermostWebhookUrl = WebhookUrlMasker.Mask(chat.MattermostWebhookUrl);
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
                // ResolveWebhook: null = "no change" (Chat.ApplyUpdate uses ?? current). The masked
                // sentinel posted back from the unchanged field, and an empty field, both mean "keep
                // the stored webhook"; only a real pasted URL overwrites.
                SlackWebhookUrl = ResolveWebhook(SlackWebhookUrl),
                MattermostWebhookUrl = ResolveWebhook(MattermostWebhookUrl),
            };

        // null  → don't change the stored webhook (ApplyUpdate: ?? current).
        // value → overwrite with the newly-pasted URL. Trims surrounding whitespace so a sloppy
        // paste isn't persisted verbatim (Uri.TryCreate trims when validating, storage did not).
        private static string ResolveWebhook(string posted)
        {
            if (string.IsNullOrWhiteSpace(posted) || WebhookUrlMasker.IsMasked(posted))
                return null;

            return posted.Trim();
        }

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
                // Route through ResolveWebhook so a masked/whitespace value can never be persisted
                // as a live webhook (a brand-new chat posts a real URL, never the mask).
                SlackWebhookUrl = ResolveWebhook(SlackWebhookUrl),
                MattermostWebhookUrl = ResolveWebhook(MattermostWebhookUrl),
            };

            return new Chat(entity);
        }
    }
}
