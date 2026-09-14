using HSMServer.Extensions;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // Locks the markup contract of ChatIcons.MattermostBrandIconHtml: the string is written
    // RAW (via ChatBrandIconsAndName's IHtmlContent) into data-content attributes and passes
    // through bootstrap-select's tag+attribute sanitizer (bootstrap-select.js,
    // DefaultWhitelist) before being rendered in chat pickers.
    public class ChatIconsTests
    {
        // A double quote would terminate the data-content attribute mid-markup and leak the
        // rest of the icon markup as stray attributes on the <option>. Nothing else fails on
        // this — the only symptom is a subtly broken dropdown.
        [Fact]
        public void MattermostBrandIconHtml_ContainsNoDoubleQuotes()
        {
            Assert.DoesNotContain("\"", ChatIcons.MattermostBrandIconHtml);
        }

        // Only whitelisted tag and attributes survive the sanitizer: svg/path have no
        // whitelisted tag, and an inline style re-introduces the per-instance base64 payload
        // (~1.6 KB per icon occurrence in HTML/JSON). The span must carry nothing but the
        // globally whitelisted class/aria attributes, with all styling in site.css
        // (.mattermost-brand-icon).
        [Fact]
        public void MattermostBrandIconHtml_UsesOnlySanitizerWhitelistedTagAndAttributes()
        {
            var html = ChatIcons.MattermostBrandIconHtml;

            Assert.StartsWith("<span ", html);
            Assert.EndsWith("</span>", html);
            Assert.Contains("class='mattermost-brand-icon'", html);
            Assert.DoesNotContain("svg", html);
            Assert.DoesNotContain("style=", html);
        }
    }
}
