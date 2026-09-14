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

        // Font Awesome Free lacks an official Mattermost brand glyph, so we ship the brand mark
        // from Simple Icons (CC0-1.0) as a CSS mask on a <span>. An inline <svg> would disappear
        // inside chat pickers: bootstrap-select sanitizes `data-content` HTML with a tag
        // whitelist that has no svg/path (bootstrap-select.js, DefaultWhitelist), while <span>
        // and its class/style attributes are whitelisted and the sanitizer never parses CSS
        // inside `style`, so the mask data URI passes through untouched. The em-sized box and
        // background-color:currentColor make the icon inherit font-size/color just like the
        // surrounding <i class='fab fa-...'> tags — both in sanitized dropdowns and wherever
        // this markup is rendered raw outside a <select>.
        private const string MattermostBrandMaskUrl =
            "data:image/svg+xml;base64," +
            "PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZpZXdCb3g9IjAgMCAyNCAyNCI+PHBhdGggZD0iTTEyLjA4" +
            "MSAwQzcuMDQ4LS4wMzQgMi4zMzkgMy4xMjUuNjM3IDguMTUzYy0yLjEyNSA2LjI3NiAxLjI0IDEzLjA4NiA3LjUxNiAxNS4yMSA2" +
            "LjI3NiAyLjEyNSAxMy4wODYtMS4yNCAxNS4yMS03LjUxNiAxLjcyNy01LjEtLjE3Mi0xMC41NTItNC4zMTEtMTMuNTU3bC4xMjYg" +
            "Mi41NDdjMi4wNjUgMi4yODIgMi44OCA1LjUxMiAxLjg1MiA4LjU0OS0xLjUzNCA0LjUzMi02LjU5NCA2LjkxNS0xMS4zIDUuMzIx" +
            "LTQuNzA4LTEuNTkzLTcuMjgtNi41NTktNS43NDUtMTEuMDkyIDEuMDMxLTMuMDQ2IDMuNjU1LTUuMTIxIDYuNjk0LTUuNjdsMS42" +
            "NDItMS45NEE0Ljg3IDQuODcgMCAwIDAgMTIuMDggMHptMy41MjggMS4wOTRhLjI4NC4yODQgMCAwIDAtLjEyMy4wMjRsLS4wMDQu" +
            "MDAxYS4zMy4zMyAwIDAgMC0uMTA5LjA3MWMtLjE0NS4xNDItLjY1Ny44MjgtLjY1Ny44MjhMMTMuNiAzLjRsLTEuMyAxLjU4NS0y" +
            "LjIzMiAyLjc3NnMtMS4wMjQgMS4yNzgtLjc5OCAyLjg1MWMuMjI2IDEuNTc0IDEuMzk2IDIuMzQgMi4zMDQgMi42NDguOTA3LjMw" +
            "NyAyLjMwMi40MDggMy40MzgtLjcwNCAxLjEzNS0xLjExMiAxLjA5OC0yLjc1IDEuMDk4LTIuNzVsLS4wODctMy41Ni0uMDctMi4w" +
            "NS0uMDQ3LTEuNzc1cy4wMS0uODU2LS4wMi0xLjA1N2EuMzMuMzMgMCAwIDAtLjAzNS0uMTA3bC0uMDA2LS4wMTItLjAwNy0uMDEx" +
            "YS4yNzcuMjc3IDAgMCAwLS4yMjktLjE0eiIvPjwvc3ZnPg==";

        // The whole string must stay free of double quotes: ChatBrandIconsAndName returns
        // IHtmlContent, which Razor writes into data-content attributes RAW — a double
        // quote would terminate the attribute mid-style (the old svg used single quotes
        // only for the same reason). CSS url() tolerates unquoted tokens, and the base64
        // alphabet never contains quotes, whitespace or parentheses.
        public const string MattermostBrandIconHtml =
            "<span aria-hidden='true' style='" +
            "display:inline-block;width:1em;height:1em;vertical-align:-.125em;background-color:currentColor;" +
            $"-webkit-mask-image:url({MattermostBrandMaskUrl});-webkit-mask-repeat:no-repeat;-webkit-mask-size:100% 100%;" +
            $"mask-image:url({MattermostBrandMaskUrl});mask-repeat:no-repeat;mask-size:100% 100%'></span>";


        public static string ChatBrandIcon(this Chat chat)
        {
            if (chat.TelegramChatId is not null)
                return $"<i class='{TelegramBrandClass}'></i>";

            if (!string.IsNullOrEmpty(chat.SlackWebhookUrl))
                return $"<i class='{SlackBrandClass}'></i>";

            if (!string.IsNullOrEmpty(chat.MattermostWebhookUrl))
                return MattermostBrandIconHtml;

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
                icons.Add(MattermostBrandIconHtml);

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
