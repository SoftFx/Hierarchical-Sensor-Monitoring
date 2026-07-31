using HSMServer.Notifications;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    public class WebhookUrlMaskerTests
    {
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Mask_NullOrEmpty_ReturnsNull(string url)
        {
            Assert.Null(WebhookUrlMasker.Mask(url));
        }

        [Fact]
        public void Mask_SlackExample_KeepsHostPathPrefixAndTail()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com/services/T0ABCDE/B0123456/abcXYZsecret");

            // host verbatim + first 8 chars of PathAndQuery (`/service`) + marker + last 4 chars (`cret`).
            Assert.Equal("https://hooks.slack.com/service••••cret", masked);
        }

        // The hard security requirement: the secret middle must not leak into the rendered mask.
        // Both the path tail segment and the secret substring sit between the visible windows.
        [Fact]
        public void Mask_SlackUrl_SecretMiddleIsAbsentFromResult()
        {
            const string secret = "abcXYZsecret";

            var masked = WebhookUrlMasker.Mask($"https://hooks.slack.com/services/T0ABCDE/B0123456/{secret}");

            Assert.DoesNotContain(secret, masked);
            Assert.DoesNotContain("B0123456", masked);
            // The visible tail ("cret") is allowed — it's only the last 4 chars, not the full secret.
        }

        [Fact]
        public void Mask_MattermostExample_KeepsHostPathPrefixAndTail()
        {
            var masked = WebhookUrlMasker.Mask("https://mattermost.example.com/hooks/abcd1234efgh5678");

            // host verbatim + first 8 chars of PathAndQuery (`/hooks/a`) + marker + last 4 chars (`5678`).
            Assert.Equal("https://mattermost.example.com/hooks/a••••5678", masked);
        }

        [Fact]
        public void Mask_MattermostUrl_SecretMiddleIsAbsentFromResult()
        {
            const string secret = "abcd1234efgh5678";

            var masked = WebhookUrlMasker.Mask($"https://mattermost.example.com/hooks/{secret}");

            // The full secret must not appear; only its 8-char path-inclusive prefix and 4-char tail
            // are visible. `/hooks/a` + `••••` + `5678`.
            Assert.DoesNotContain(secret, masked);
            Assert.Contains("5678", masked);
        }

        // A webhook URL with no path collapses to host + marker — the marker MUST be present so the
        // POST sentinel detection (IsMasked) works for every masked value, and an empty path has
        // nothing recognition-worthy to expose anyway.
        [Fact]
        public void Mask_UrlWithoutPath_ShowsHostAndMarkerOnly()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com");

            Assert.Equal("https://hooks.slack.com/••••", masked);
            Assert.True(WebhookUrlMasker.IsMasked(masked));
        }

        // A short path (≤ prefix+tail threshold) is also collapsed to the marker — showing it
        // verbatim would leak the whole short secret, and the marker must be present for IsMasked.
        [Fact]
        public void Mask_UrlWithShortPath_ShowsHostAndMarkerOnly()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com/short");

            Assert.Equal("https://hooks.slack.com/••••", masked);
            Assert.True(WebhookUrlMasker.IsMasked(masked));
        }

        // Weird/unparseable input must mask rather than throw — a stored webhook should never be
        // unparseable, but the helper is a boundary and must degrade safely.
        [Fact]
        public void Mask_NonParseableInput_DoesNotThrowAndMasks()
        {
            var masked = WebhookUrlMasker.Mask("not-a-url-with-a-slash/and/secret/tail-value");

            Assert.NotNull(masked);
            Assert.Contains(WebhookUrlMasker.MaskMarker, masked);
            Assert.DoesNotContain("secret", masked);
        }

        [Theory]
        [InlineData("https://hooks.slack.com/services/••••")]
        [InlineData("prefix••••suffix")]
        public void IsMasked_ValueContainsMarker_ReturnsTrue(string url)
        {
            Assert.True(WebhookUrlMasker.IsMasked(url));
        }

        [Theory]
        [InlineData("https://hooks.slack.com/services/real-url")]
        [InlineData("https://mattermost.example.com/hooks/abcd1234")]
        [InlineData("")]
        public void IsMasked_RealUrlOrEmpty_ReturnsFalse(string url)
        {
            Assert.False(WebhookUrlMasker.IsMasked(url));
        }

        [Fact]
        public void IsMasked_Null_ReturnsFalse()
        {
            Assert.False(WebhookUrlMasker.IsMasked(null));
        }
    }
}
