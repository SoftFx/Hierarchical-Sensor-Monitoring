using Xunit;
using HSMServer.Core.Services;


namespace HSMServer.Core.Tests.ConverterTests
{
    public class HtmlSanitizerTests
    {

        [Theory]
        [InlineData("<","&lt;")]
        [InlineData("<a href='ya.ru'>link</a>", "link")]
        [InlineData("<b>link</b>", "link")]
        public void HtmlSanitizerTest(string input, string expected)
        {

            var result = HtmlSanitizerService.Sanitize(input);
            Assert.Equal(expected, result);
        }


    }
}
