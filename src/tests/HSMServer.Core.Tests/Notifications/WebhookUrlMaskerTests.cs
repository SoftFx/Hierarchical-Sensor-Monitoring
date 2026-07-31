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
        public void Mask_SlackExample_KeepsSchemeHostAndFirstSegment_MasksRest()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com/services/T0ABCDE/B0123456/abcXYZsecret");

            Assert.Equal("https://hooks.slack.com/services/••••", masked);
        }

        // The hard security requirement: the secret tail must not leak into the rendered mask.
        [Fact]
        public void Mask_SlackUrl_SecretTailIsAbsentFromResult()
        {
            const string secret = "abcXYZsecret";

            var masked = WebhookUrlMasker.Mask($"https://hooks.slack.com/services/T0ABCDE/B0123456/{secret}");

            Assert.DoesNotContain(secret, masked);
            Assert.DoesNotContain("B0123456", masked);
        }

        [Fact]
        public void Mask_MattermostExample_KeepsSchemeHostAndFirstSegment()
        {
            var masked = WebhookUrlMasker.Mask("https://mattermost.example.com/hooks/abcd1234efgh5678");

            Assert.Equal("https://mattermost.example.com/hooks/••••", masked);
        }

        [Fact]
        public void Mask_MattermostUrl_SecretTailIsAbsentFromResult()
        {
            const string secret = "abcd1234efgh5678";

            var masked = WebhookUrlMasker.Mask($"https://mattermost.example.com/hooks/{secret}");

            Assert.DoesNotContain(secret, masked);
        }

        // A webhook URL with no path segment still gets the marker, so the POST-sentinel detection in
        // ToUpdate works uniformly (any masked value ends in `••••`).
        [Fact]
        public void Mask_UrlWithoutPath_StillAppendsMarker()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com");

            Assert.Equal("https://hooks.slack.com/••••", masked);
        }

        [Fact]
        public void Mask_UrlWithOnlyRootPath_StillAppendsMarker()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com/");

            Assert.Equal("https://hooks.slack.com/••••", masked);
        }

        // Weird/unparseable input must mask rather than throw — a stored webhook should never be
        // unparseable, but the helper is a boundary and must degrade safely.
        [Fact]
        public void Mask_NonParseableInput_DoesNotThrowAndMasks()
        {
            var masked = WebhookUrlMasker.Mask("not-a-url-with-a-slash/and/secret/tail");

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
