using HSMServer.Helpers;
using Xunit;

namespace HSMServer.Core.Tests.ConverterTests
{
    public class MarkdownConverterTests
    {

        [Theory]
        [InlineData("**жирный**", "*жирный*")]
        [InlineData("__жирный__", "*жирный*")]
        [InlineData("*курсив*", "_курсив_")]
        [InlineData("_курсив_", "_курсив_")]
        [InlineData("`код`", "`код`")]
        [InlineData("~~зачеркнутый~~", "~зачеркнутый~")]
        [InlineData("Текст со _*[]()~`>#+-=|{}.!",
           "Текст со \\_\\*\\[\\]\\(\\)\\~\\`\\>\\#\\+\\-\\=\\|\\{\\}\\.\\!")]
        [InlineData("[текст](https://example.com)", "[текст](https://example.com)")]
        [InlineData("**жирный _с курсивом_**", "*жирный \\_с курсивом\\_*")]
        [InlineData("", "")]
        [InlineData("   ", "   ")]
        [InlineData("_", "\\_")]
        [InlineData("*", "\\*")]
        [InlineData("**незакрытый", "\\*\\*незакрытый")]
        [InlineData("__", "\\_\\_")]
        [InlineData("**😊**", "*😊*")]
        [InlineData("_中文_", "_中文_")]
        [InlineData(@"\*", @"\\*")]
        [InlineData(@"\\_", @"\\\_")]
        [InlineData(@"\\\*", @"\\\\*")]
        [InlineData(@"\**", @"\\*\*")]
        [InlineData(@"\_italic\_", @"\\_italic\\_")]
        [InlineData(@"[A1] Value > 0", @"\[A1\] Value \> 0")]
        public void ConvertsCorrectly(string input, string expected)
        {
            var result = MarkdownHelper.ConvertToMarkdownV2(input);
            Assert.Equal(expected, result);
        }

    }
}
