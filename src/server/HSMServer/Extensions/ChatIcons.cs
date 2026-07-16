using HSMServer.Notifications.Chats;
using System.Collections.Generic;
using System.Text;

namespace HSMServer.Extensions
{
    public static class ChatIcons
    {
        public const string TelegramBrandClass = "fab fa-telegram";

        public const string SlackBrandClass = "fab fa-slack";

        public const string MattermostBrandClass = "fab fa-" + "mattermost"; // Font Awesome lacks an official Mattermost brand; consumers can override.


        public static string ChatBrandClass(this Chat chat)
        {
            if (chat.TelegramChatId is not null)
                return TelegramBrandClass;

            if (!string.IsNullOrEmpty(chat.SlackWebhookUrl))
                return SlackBrandClass;

            if (!string.IsNullOrEmpty(chat.MattermostWebhookUrl))
                return MattermostBrandClass;

            return null;
        }

        public static string ChatBrandIcon(this Chat chat)
        {
            var brand = chat.ChatBrandClass();
            return string.IsNullOrEmpty(brand) ? null : $"<i class='{brand}'></i>";
        }

        public static string ChatBrandIcons(this Chat chat)
        {
            var icons = new List<string>(3);

            if (chat.TelegramChatId is not null)
                icons.Add($"<i class='{TelegramBrandClass}'></i>");

            if (!string.IsNullOrEmpty(chat.SlackWebhookUrl))
                icons.Add($"<i class='{SlackBrandClass}'></i>");

            if (!string.IsNullOrEmpty(chat.MattermostWebhookUrl))
                icons.Add($"<i class='{MattermostBrandClass}'></i>");

            if (icons.Count == 0)
                return null;

            var sb = new StringBuilder();
            foreach (var icon in icons)
                sb.Append(icon).Append(' ');

            return sb.ToString(0, sb.Length - 1);
        }
    }
}
