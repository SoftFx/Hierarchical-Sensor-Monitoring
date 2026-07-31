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

        // The canonical example from the review discussion: Mattermost-style URL where the last path
        // segment is the secret token. Scheme + host + preceding path segments stay verbatim; only
        // the last segment is reduced to head4 + `••••` + tail4.
        [Fact]
        public void Mask_LocalhostMattermostExample_MasksOnlyLastSegment()
        {
            var masked = WebhookUrlMasker.Mask("http://localhost:8065/hooks/nuddbzeoztbau8juhobap348fw");

            Assert.Equal("http://localhost:8065/hooks/nudd••••48fw", masked);
        }

        [Fact]
        public void Mask_SlackMultiSegmentUrl_MasksOnlyLastSegment()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com/services/T0ABCDE/B0123456/abcXYZsecret");

            // All three path segments before the last stay verbatim; only `abcXYZsecret` is masked.
            Assert.Equal("https://hooks.slack.com/services/T0ABCDE/B0123456/abcX••••cret", masked);
        }

        // The hard security requirement: the secret middle of the last segment must not leak into
        // the rendered mask. Only the full `secret` token must be absent — preceding path segments
        // (B0123456) are structural and intentionally visible per the new format.
        [Fact]
        public void Mask_SlackUrl_SecretMiddleIsAbsentFromResult()
        {
            const string secret = "abcXYZsecret";

            var masked = WebhookUrlMasker.Mask($"https://hooks.slack.com/services/T0ABCDE/B0123456/{secret}");

            Assert.DoesNotContain(secret, masked);
            // The visible head/tail of the last segment ("abcX", "cret") are allowed — they're only
            // 4 chars each, not the full secret.
        }

        [Fact]
        public void Mask_MattermostUrl_MasksOnlyLastSegment()
        {
            var masked = WebhookUrlMasker.Mask("https://mattermost.example.com/hooks/abcd1234efgh5678");

            Assert.Equal("https://mattermost.example.com/hooks/abcd••••5678", masked);
        }

        [Fact]
        public void Mask_MattermostUrl_SecretMiddleIsAbsentFromResult()
        {
            const string secret = "abcd1234efgh5678";

            var masked = WebhookUrlMasker.Mask($"https://mattermost.example.com/hooks/{secret}");

            // The full secret must not appear; only its 4-char head and 4-char tail are visible.
            Assert.DoesNotContain(secret, masked);
            Assert.Contains("abcd", masked);
            Assert.Contains("5678", masked);
        }

        // Regression: a trailing slash used to defeat masking entirely. The old string-slicing took
        // url.LastIndexOf('/'), found the trailing slash, and returned the full token verbatim with
        // just a marker appended — the secret was in the page source and the input value attribute,
        // exactly the leak #1329 closes. Segment-based masking (via Uri.Segments) walks back past the
        // empty trailing segment to the real token and masks it.
        [Fact]
        public void Mask_TrailingSlash_MasksTheRealLastSegmentNotTheEmptyTail()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com/services/T0ABCDE/B0123456/abcXYZsecret/");

            Assert.Equal("https://hooks.slack.com/services/T0ABCDE/B0123456/abcX••••cret", masked);
        }

        [Fact]
        public void Mask_TrailingSlash_SecretTokenIsAbsentFromResult()
        {
            const string secret = "abcXYZsecret";

            var masked = WebhookUrlMasker.Mask($"https://hooks.slack.com/services/T0ABCDE/B0123456/{secret}/");

            Assert.DoesNotContain(secret, masked);
        }

        // Regression: a query string containing '/' used to move the split point past the token
        // (`…/SECRETTOKEN?redirect=/x` masked the query, not the token). The query is now dropped
        // from the display value entirely and the token segment is masked.
        [Fact]
        public void Mask_QueryStringWithSlash_MasksTheTokenNotTheQuery()
        {
            var masked = WebhookUrlMasker.Mask("https://host/hooks/SECRETTOKEN?redirect=/x");

            Assert.Equal("https://host/hooks/SECR••••OKEN", masked);
        }

        [Fact]
        public void Mask_Fragment_MasksTheTokenNotTheFragment()
        {
            var masked = WebhookUrlMasker.Mask("https://host/hooks/SECRETTOKEN#section");

            Assert.Equal("https://host/hooks/SECR••••OKEN", masked);
        }

        // A path that is only root segments (`/`) collapses to authority + marker — same behavior as
        // a URL with no path at all.
        [Fact]
        public void Mask_RootPathOnly_ShowsAuthorityAndMarker()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com/");

            Assert.Equal("https://hooks.slack.com/••••", masked);
            Assert.True(WebhookUrlMasker.IsMasked(masked));
        }

        // A short last segment (≤ 8 chars = head+tail threshold) is shown verbatim per UX decision,
        // with the marker appended as a trailing sentinel so IsMasked stays true (otherwise the
        // round-tripped value would look like a fresh URL and overwrite the stored webhook on save).
        [Fact]
        public void Mask_LastSegmentShortEnough_ShownVerbatimWithTrailingMarker()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com/services/short");

            Assert.Equal("https://hooks.slack.com/services/short••••", masked);
            Assert.True(WebhookUrlMasker.IsMasked(masked));
        }

        // A webhook URL with no path (no '/' after the host) collapses to authority + marker. Uri
        // still reports Segments=["/"] for the implicit root, so the slash appears before the marker;
        // nothing recognition-worthy is leaked since there's no token to show.
        [Fact]
        public void Mask_UrlWithoutPath_AppendsMarker()
        {
            var masked = WebhookUrlMasker.Mask("https://hooks.slack.com");

            Assert.Equal("https://hooks.slack.com/••••", masked);
            Assert.True(WebhookUrlMasker.IsMasked(masked));
        }

        // Weird/unparseable input must mask rather than throw — a stored webhook should never be
        // unparseable, but the helper is a boundary and must degrade safely. It still slices at the
        // last '/' so the trailing segment is masked.
        [Fact]
        public void Mask_NonParseableInputWithSlashes_DoesNotThrowAndMasksLastSegment()
        {
            var masked = WebhookUrlMasker.Mask("not-a-url/and/secret-tail-value-here");

            Assert.NotNull(masked);
            Assert.Contains(WebhookUrlMasker.MaskMarker, masked);
            Assert.DoesNotContain("secret-tail-value-here", masked);
        }

        [Theory]
        [InlineData("https://hooks.slack.com/services/abcX••••cret")]
        [InlineData("https://hooks.slack.com/services/short••••")]
        [InlineData("prefix••••suffix")]
        public void IsMasked_ValueContainsMarker_ReturnsTrue(string url)
        {
            Assert.True(WebhookUrlMasker.IsMasked(url));
        }

        [Theory]
        [InlineData("https://hooks.slack.com/services/real-url-token")]
        [InlineData("https://mattermost.example.com/hooks/abcd1234efgh5678")]
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
