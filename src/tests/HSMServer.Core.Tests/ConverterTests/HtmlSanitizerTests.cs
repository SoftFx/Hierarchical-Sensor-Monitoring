using HSMServer.Helpers;
using HSMServer.Services;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class HtmlSanitizerTests
    {
        private HtmlSanitizerService _sanitizer = new HtmlSanitizerService();

        [Theory]
        [InlineData("<","&lt;")]
        [InlineData("<a href='ya.ru'>link</a>", "link")]
        [InlineData("<b>link</b>", "link")]
        public void HtmlSanitizerTest(string input, string expected)
        {

            var result = _sanitizer.Sanitize(input);
            Assert.Equal(expected, result);
        }


    }
}
