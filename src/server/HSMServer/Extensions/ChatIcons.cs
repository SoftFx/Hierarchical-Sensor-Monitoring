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
        // from Simple Icons (CC0-1.0) as a CSS-masked <span>. Chat pickers pass icon markup
        // through bootstrap-select's `data-content` sanitizer, which keeps only whitelisted
        // tags AND attributes (bootstrap-select.js, DefaultWhitelist) — an inline <svg> has no
        // whitelisted tag, so it is silently deleted. The span therefore carries only globally
        // whitelisted attributes — class, role, aria-* — while all styling — the base64 mask,
        // the em-sized box, the currentColor fill — lives in site.css under
        // .mattermost-brand-icon: the data URI ships once in the cached bundle instead of per
        // icon instance in HTML/JSON responses, and the icon still inherits font-size/color
        // just like the surrounding <i class='fab fa-...'> tags.
        //
        // role='img' + aria-label='Mattermost' keep the accessible name the removed inline SVG
        // carried via role='img' + <title>Mattermost</title>: at several render sites
        // (Configuration/_Chats, Product/_FolderAccordion, Shared/_DefaultChat) the brand icon
        // is the only thing telling which channel a chat uses. A <title> element would NOT
        // work here — the sanitizer whitelists it only on a/img, so it would be stripped
        // inside pickers while surviving at raw-render sites (inconsistent markup) — whereas
        // role and aria-* are on the global whitelist and survive everywhere.
        //
        // The whole string must stay free of double quotes: ChatBrandIconsAndName returns
        // IHtmlContent, which Razor writes into data-content attributes RAW — a double
        // quote would terminate the attribute mid-markup and leak the rest as stray
        // attributes. Pinned by ChatIconsTests.
        public const string MattermostBrandIconHtml =
            "<span class='mattermost-brand-icon' role='img' aria-label='Mattermost'></span>";


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
        // This method returns IHtmlContent (HtmlString), so Razor writes the value into the
        // attribute RAW — no HtmlEncoding pass, no encode → attribute-decode round trip. (An
        // ordinary string WOULD be round-tripped that way and still land in the DOM as markup —
        // the reason we cannot lean on Razor encoding for safety.) What we return here is
        // exactly what bootstrap-select later inserts via `innerHTML` (bootstrap-select.js:748).
        //
        // chat.Name is user-controlled. Because the value reaches that innerHTML call raw, any
        // "<script>" / "<img onerror=...>" in the name would execute. To prevent that we
        // double-encode the name ourselves: attribute decode undoes one layer, the innerHTML
        // HTML-entity decode undoes the second, leaving "&lt;" / "&gt;" entities in the parsed
        // markup that the browser renders as inert text.
        public static IHtmlContent ChatBrandIconsAndName(this Chat chat)
        {
            var icons = chat.ChatBrandIcons() ?? string.Empty;
            var rawName = chat.Name ?? string.Empty;
            var safeName = WebUtility.HtmlEncode(WebUtility.HtmlEncode(rawName));
            return new HtmlString($"{icons} {safeName}");
        }
    }
}
