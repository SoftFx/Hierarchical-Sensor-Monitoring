using System;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Extensions;
using HSMServer.Model.Notifications;
using HSMServer.Notifications;
using HSMServer.Notifications.Chats;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // Locks the markup contract of ChatIcons: everything ChatBrandIcons()/ChatBrandIconsAndName()
    // emit is written RAW (via IHtmlContent) into data-content attributes and passes through
    // bootstrap-select's tag+attribute sanitizer (bootstrap-select.js, DefaultWhitelist) before
    // being rendered in chat pickers.
    public class ChatIconsTests
    {
        // A double quote anywhere in the composed icons markup would terminate the data-content
        // attribute mid-markup and leak the rest as stray attributes on the <option>. The rule
        // binds everything ChatBrandIcons() concatenates, not just the Mattermost constant —
        // TelegramBrandClass/SlackBrandClass are interpolated into <i class='...'> on the same
        // string and would break the attribute the same way — so assert on the composed output
        // of a chat carrying all three channels. Nothing else fails on this; the only symptom
        // is a subtly broken dropdown.
        [Fact]
        public void ChatBrandIcons_AllChannels_ComposedMarkupContainsNoDoubleQuotes()
        {
            var chat = BuildChat(telegram: true, slack: true, mattermost: true);

            var html = chat.ChatBrandIcons();

            Assert.DoesNotContain("\"", html);
            Assert.Equal(
                $"<i class='{ChatIcons.TelegramBrandClass}'></i> " +
                $"<i class='{ChatIcons.SlackBrandClass}'></i> " +
                ChatIcons.MattermostBrandIconHtml,
                html);
        }

        // Only whitelisted tags and attributes survive the sanitizer: svg/path have no
        // whitelisted tag, and an inline style would re-introduce the per-instance base64
        // payload (~1.6 KB per icon occurrence in HTML/JSON). The span must carry nothing but
        // the globally whitelisted class/role/aria attributes, with all styling in site.css
        // (.mattermost-brand-icon). svg/path are asserted explicitly — the literal shape
        // #1359 was about: the sanitizer deletes them silently, so nothing else would fail.
        // role='img' + aria-label pin the accessible name (PR #1388 review finding: the
        // removed inline SVG carried role='img' + <title>Mattermost</title>, and at several
        // render sites the brand icon is the only channel indicator).
        [Fact]
        public void MattermostBrandIconHtml_ContainsOnlyWhitelistedTagAndAttributes()
        {
            var html = ChatIcons.MattermostBrandIconHtml;

            Assert.StartsWith("<span ", html);
            Assert.EndsWith("</span>", html);
            Assert.Contains("class='mattermost-brand-icon'", html);
            Assert.Contains("role='img'", html);
            Assert.Contains("aria-label='Mattermost'", html);
            Assert.DoesNotContain("style=", html);
            Assert.DoesNotContain("<svg", html);
            Assert.DoesNotContain("<path", html);
        }

        // ChatViewModel carries a SECOND ChatBrandIcons implementation (used by the
        // Configuration/_Chats list rows) composed from its own Has* flags. It feeds the same
        // raw data-content surface, so pin it to the same contract — without this the two
        // implementations can drift apart with no test noticing.
        [Fact]
        public void ChatViewModelChatBrandIcons_AllChannels_MatchesExtensionMethodOutput()
        {
            var chat = BuildChat(telegram: true, slack: true, mattermost: true);
            var viewModel = new ChatViewModel
            {
                TelegramChatId = chat.TelegramChatId!.Identifier,
                SlackWebhookUrl = chat.SlackWebhookUrl,
                MattermostWebhookUrl = chat.MattermostWebhookUrl,
            };

            var html = viewModel.ChatBrandIcons();

            Assert.DoesNotContain("\"", html);
            Assert.Equal(chat.ChatBrandIcons(), html);
        }

        private static Chat BuildChat(bool telegram = false, bool slack = false, bool mattermost = false) =>
            new(new ChatEntity
            {
                Id = Guid.NewGuid().ToByteArray(),
                Author = Guid.NewGuid().ToByteArray(),
                CreationDate = DateTime.UtcNow.Ticks,
                Name = "chat",
                SendMessages = true,
                MessagesAggregationTimeSec = 60,
                TelegramChatId = telegram ? 999_999L : null,
                TelegramType = telegram ? (byte)ConnectedChatType.TelegramPrivate : null,
                SlackWebhookUrl = slack ? "https://hooks.slack.com/services/test" : null,
                MattermostWebhookUrl = mattermost ? "https://mattermost.example.com/hooks/test" : null,
            });
    }
}
