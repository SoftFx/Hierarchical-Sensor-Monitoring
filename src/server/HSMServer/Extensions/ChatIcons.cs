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

        // Font Awesome Free lacks an official Mattermost brand icon, so we inline the brand
        // mark from Simple Icons (CC0-1.0). Sized with em units and currentColor so it inherits
        // font-size/color just like the surrounding <i class='fab fa-...'> tags.
        public const string MattermostBrandIconSvg =
            "<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24' " +
            "width='1em' height='1em' fill='currentColor' aria-hidden='true' " +
            "role='img' focusable='false' style='vertical-align:-.125em'>" +
            "<title>Mattermost</title>" +
            "<path d='M12.081 0C7.048-.034 2.339 3.125.637 8.153c-2.125 6.276 1.24 13.086 7.516 15.21 6.276 2.125 13.086-1.24 15.21-7.516 1.727-5.1-.172-10.552-4.311-13.557l.126 2.547c2.065 2.282 2.88 5.512 1.852 8.549-1.534 4.532-6.594 6.915-11.3 5.321-4.708-1.593-7.28-6.559-5.745-11.092 1.031-3.046 3.655-5.121 6.694-5.67l1.642-1.94A4.87 4.87 0 0 0 12.08 0zm3.528 1.094a.284.284 0 0 0-.123.024l-.004.001a.33.33 0 0 0-.109.071c-.145.142-.657.828-.657.828L13.6 3.4l-1.3 1.585-2.232 2.776s-1.024 1.278-.798 2.851c.226 1.574 1.396 2.34 2.304 2.648.907.307 2.302.408 3.438-.704 1.135-1.112 1.098-2.75 1.098-2.75l-.087-3.56-.07-2.05-.047-1.775s.01-.856-.02-1.057a.33.33 0 0 0-.035-.107l-.006-.012-.007-.011a.277.277 0 0 0-.229-.14z'/>" +
            "</svg>";


        public static string ChatBrandIcon(this Chat chat)
        {
            if (chat.TelegramChatId is not null)
                return $"<i class='{TelegramBrandClass}'></i>";

            if (!string.IsNullOrEmpty(chat.SlackWebhookUrl))
                return $"<i class='{SlackBrandClass}'></i>";

            if (!string.IsNullOrEmpty(chat.MattermostWebhookUrl))
                return MattermostBrandIconSvg;

            return null;
        }

        public static string ChatBrandIcons(this Chat chat)
        {
            var icons = new List<string>(3);

            if (chat.TelegramChatId is not null)
                icons.Add($"<i class='{TelegramBrandClass}'></i>");

            if (!string.IsNullOrEmpty(chat.SlackWebhookUrl))
                icons.Add($"<i class='{SlackBrandClass}'></i>");

            if (!string.IsNullOrEmpty(chat.MattermostWebhookUrl))
                icons.Add(MattermostBrandIconSvg);

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
