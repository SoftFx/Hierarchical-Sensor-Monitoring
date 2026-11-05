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
        public void IsMatch_AfterFailedApply_NeverThrows()
        {
            // The pre-#1607 failure mode: the pattern compiled lazily, so the
            // FIRST candidate path blew up with RegexParseException. After a
            // failed apply the converter simply holds no pattern.
            var converter = new PathTemplateConverter();

            Assert.False(converter.ApplyNewTemplate("HSM Server Monitoring/([", out _));

            var matched = converter.IsMatch("HSM Server Monitoring/cpu");

            Assert.True(matched); // no pattern applied: the converter's match-everything state
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
