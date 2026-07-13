using System;
using HSMServer.Notifications;

namespace HSMServer.Extensions
{
    public static class ChatIcons
    {
        public const string TelegramBrandClass = "fab fa-telegram";

        public const string SlackBrandClass = "fab fa-slack";


        public static string ChatBrandClass(this Guid id, ITelegramChatsManager telegram, ISlackDestinationsManager slack)
        {
            if (telegram.TryGetValue(id, out _))
                return TelegramBrandClass;

            if (slack.TryGetValue(id, out _))
                return SlackBrandClass;

            return null;
        }

        public static string ChatBrandIcon(this Guid id, ITelegramChatsManager telegram, ISlackDestinationsManager slack)
        {
            var brand = id.ChatBrandClass(telegram, slack);
            return string.IsNullOrEmpty(brand) ? null : $"<i class='{brand}'></i>";
        }
    }
}
