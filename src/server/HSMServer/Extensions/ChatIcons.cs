using HSMServer.Notifications.Chats;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;

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

        // Builds the value for the bootstrap-select `data-content` attribute on a chat <option>:
        // the multi-channel brand icons followed by the chat name.
        //
        // Razor HtmlEncodes any string it emits into an attribute, so the icon markup (e.g.
        // "<i class='fab fa-telegram'></i>") is round-tripped through encode → browser attribute
        // decode and lands in the DOM as raw markup — exactly what bootstrap-select inserts via
        // `innerHTML` (bootstrap-select.js:748).
        //
        // chat.Name is user-controlled. The same encode → attribute-decode round trip that
        // restores the icon markup would also restore any "<script>" / "<img onerror=...>" in the
        // name, and innerHTML would then execute it. To prevent that we return an IHtmlContent
        // (so Razor does not re-encode) and double-encode the name ourselves: attribute decode
        // undoes one layer, the innerHTML HTML-entity decode undoes the second, leaving "&lt;" /
        // "&gt;" entities in the parsed markup that the browser renders as inert text.
        public static IHtmlContent ChatBrandIconsAndName(this Chat chat)
        {
            var icons = chat.ChatBrandIcons() ?? string.Empty;
            var rawName = chat.Name ?? string.Empty;
            var safeName = WebUtility.HtmlEncode(WebUtility.HtmlEncode(rawName));
            return new HtmlString($"{icons} {safeName}");
        }
    }
}
