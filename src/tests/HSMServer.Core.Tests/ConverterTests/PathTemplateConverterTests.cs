using HSMServer.PathTemplates;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    // #1607: a path pattern's regex is compiled EAGERLY in ApplyNewTemplate —
    // a malformed pattern (regex metacharacters inside a constant part, e.g. an
    // unterminated '[' set) must fail the BUILD with a message the callers can
    // report under the `paths` key, instead of throwing RegexParseException out
    // of IsMatch on the first candidate path (which escaped the template write
    // engine as a bare MCP tool error / a REST 500).
    public class PathTemplateConverterTests
    {
        [Theory]
        [InlineData("HSM Server Monitoring/([")] // the #1607 repro: unterminated [] set
        [InlineData("Root/cpu[")]
        [InlineData("Root/[abc/cpu")]
        public void ApplyNewTemplate_MalformedRegex_FailsWithReportableMessage(string template)
        {
            var converter = new PathTemplateConverter();

            var applied = converter.ApplyNewTemplate(template, out var error);

            Assert.False(applied);
            Assert.False(string.IsNullOrEmpty(error));
            // The reason names the invalid compiled pattern, so the field-keyed
            // error text is actionable without the raw input at hand.
            Assert.Contains("Invalid pattern", error);
        }

        [Fact]
        public void IsMatch_AfterFailedApply_MatchesNothing()
        {
            // A failed apply must leave the converter in a match-NOTHING state:
            // "no template was ever applied" (match everything) and "the last
            // template was rejected" are different, and the dashboard-panel
            // subscriptions keep querying IsMatch after a failed apply — a
            // rejected pattern must not silently degrade into match-all
            // (#1607 review).
            var converter = new PathTemplateConverter();

            Assert.False(converter.ApplyNewTemplate("HSM Server Monitoring/([", out _));

            Assert.False(converter.IsMatch("HSM Server Monitoring/cpu"));
            Assert.False(converter.IsMatch("anything/else"));
        }

        [Fact]
        public void IsMatch_NeverAppliedTemplate_MatchesEverything()
        {
            // The untouched converter has no filter at all: every path is a
            // candidate (the state a fresh PanelSubscription starts from).
            var converter = new PathTemplateConverter();

            Assert.True(converter.IsMatch("HSM Server Monitoring/cpu"));
        }

        [Fact]
        public void IsMatch_FailedApplyThenValidApply_MatchesAgain()
        {
            // The failure flag is per-apply: a later VALID template must clear
            // the match-nothing state, so editing a broken subscription
            // template back to a working one restores the filter.
            var converter = new PathTemplateConverter();

            Assert.False(converter.ApplyNewTemplate("Root/([", out _));
            Assert.True(converter.ApplyNewTemplate("*/cpu", out _));

            Assert.True(converter.IsMatch("Root/cpu"));
            Assert.False(converter.IsMatch("Root/mem"));
        }

        [Fact]
        public void ApplyNewTemplate_ValidPattern_MatchesBySegmentRules()
        {
            var converter = new PathTemplateConverter();

            Assert.True(converter.ApplyNewTemplate("*/cpu", out var error));

            Assert.Null(error);
            Assert.True(converter.IsMatch("Root/cpu"));
            Assert.False(converter.IsMatch("Root/mem"));
        }
    }
}
