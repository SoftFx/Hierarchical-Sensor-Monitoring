using System.Reflection;
using Xunit;

namespace HSMDataCollector.Tests
{
    /// <summary>
    /// Issue #1087 E: <c>DefaultPrototype.BuildPath</c> was changed (commit 80156e3ad) from
    /// <c>parts.Select(u =&gt; u?.Trim('/'))</c> to a <c>SelectMany(u =&gt; u.Split('/',
    /// RemoveEmptyEntries))</c> flatten. The two implementations agree on most inputs but
    /// disagree when a segment contains interior <c>//</c> — the old form preserved the empty
    /// segment, the new form collapses it. This is a normalization improvement (HSM paths use
    /// <c>/</c> as a separator; <c>//</c> is malformed), but the contract was undocumented and a
    /// test only existed implicitly via the default-sensor smoke suite.
    ///
    /// Document the chosen behaviour explicitly here so any future change to BuildPath fails fast
    /// and on purpose, not as a downstream test regression in some unrelated suite.
    /// </summary>
    public sealed class DefaultPrototypeBuildPathTests
    {
        [Theory]
        // Basic join.
        [InlineData(new[] { "a", "b", "c" }, "a/b/c")]
        // Null parts dropped.
        [InlineData(new[] { "a", null, "c" }, "a/c")]
        // Empty / whitespace-only parts dropped.
        [InlineData(new[] { "a", "", "c" }, "a/c")]
        [InlineData(new[] { "a", "   ", "c" }, "a/c")]
        // Leading and trailing slashes trimmed.
        [InlineData(new[] { "/a", "b/", "/c/" }, "a/b/c")]
        // Single interior slashes preserved (segment was already a sub-path).
        [InlineData(new[] { "a", "b/c", "d" }, "a/b/c/d")]
        // Double interior slashes collapsed (the behaviour change vs the original Trim('/')
        // implementation — documents the normalization choice).
        [InlineData(new[] { "a", "b//c", "d" }, "a/b/c/d")]
        // All parts dropped → empty result.
        [InlineData(new string[] { null, "", "   " }, "")]
        // Single part with both leading and trailing slashes.
        [InlineData(new[] { "//a//" }, "a")]
        public void BuildPath_normalizes_inputs(string[] parts, string expected)
        {
            // BuildPath is internal static; HSMDataCollector.Tests has InternalsVisibleTo, but the
            // method lives in HSMDataCollector.Prototypes (internal class), so call via reflection
            // to keep this test free of a using-clauses dance and stable against namespace moves.
            var asm = typeof(HSMDataCollector.Core.DataCollector).Assembly;
            var type = asm.GetType("HSMDataCollector.Prototypes.DefaultPrototype", throwOnError: true);
            var method = type.GetMethod("BuildPath", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);

            var actual = (string)method.Invoke(null, new object[] { parts });
            Assert.Equal(expected, actual);
        }
    }
}
