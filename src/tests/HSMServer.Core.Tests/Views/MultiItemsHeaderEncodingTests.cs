using HSMServer.Extensions;
using Xunit;

namespace HSMServer.Core.Tests.Views
{
    // The header of the mute dialog lists the names and paths of the items it was opened for. They come
    // from collectors and users, and the helper returns ready HTML: each item is encoded as text.
    public class MultiItemsHeaderEncodingTests
    {
        private const string Markup = "<img src=x onerror=alert(1)>";

        [Fact]
        public void Items_AreEncodedAsText()
        {
            var header = HtmlHelperExtensions.MultiItemsHeader(null, ["product/" + Markup, "plain & simple"]).ToString();

            Assert.DoesNotContain("<img", header);
            Assert.Contains("product/&lt;img src=x onerror=alert(1)&gt;", header);
            Assert.Contains("plain &amp; simple", header);
        }

        [Fact]
        public void TheHelpersOwnMarkup_IsKept()
        {
            var header = HtmlHelperExtensions.MultiItemsHeader(null, ["a", "b", "c"], maxCount: 2, useLineBreak: true).ToString();

            Assert.Equal("a, <br>b<br>... and other 1", header);
        }
    }
}
