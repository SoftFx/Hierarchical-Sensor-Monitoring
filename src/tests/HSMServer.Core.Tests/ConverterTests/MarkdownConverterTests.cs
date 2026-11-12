using System;
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
        [InlineData("Текст со _*[]()~`>#+-=|{}.!", "Текст со \\_\\*\\[\\]\\(\\)\\~\\`\\>\\#\\+\\-\\=\\|\\{\\}\\.\\!")]
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
        [InlineData(@"\*", @"\*")]
        [InlineData(@"\\_", @"\\_")]
        [InlineData(@"\\\*", @"\\\*")]
        [InlineData(@"\**", @"\*\*")]
        [InlineData(@"\_italic\_", @"\_italic\_")]
        [InlineData(@"[A1] Value > 0", @"\[A1\] Value \> 0")]
        public void ConvertsCorrectly(string input, string expected)
        {
            var result = MarkdownHelper.ConvertToMarkdownV2($"<markdown>{input}</markdown>");
            Assert.Equal(expected, result);
        }


        [Theory]
        [InlineData("[product]<markdown>[link](ya.ru)</markdown>",@"\[product\][link](ya.ru)")]
        [InlineData("[]<>", @"\[\]<\>")]
        [InlineData("<>", @"<\>")]
        [InlineData("&lt;&gt;", @"<\>")]
        public void ConvertMessageToMarkdownV2(string input, string expected)
        {
            var result = MarkdownHelper.ConvertToMarkdownV2(input);
            Assert.Equal(expected, result);
        }

        // #1705: ToHtml renders into innerHTML on the alert paths (the
        // sensor-page action block and the test toast). The <markdown> spans
        // keep their whitelist; the text AROUND them is stripped of tags
        // (the ingestion sanitizer's own rules — markdown and br stay), so a
        // stored <script> or <img onerror> in a policy template becomes
        // inert text.
        [Fact]
        public void ToHtml_PlainHtmlOutsideMarkdownSpans_IsTagStripped()
        {
            var html = MarkdownHelper.ToHtml("pre <script>alert(1)</script> hello <img src=x onerror=alert(2)> post").ToString();

            Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("onerror", html, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("hello", html, StringComparison.Ordinal);
            Assert.Contains("alert(1)", html, StringComparison.Ordinal); // the TEXT survives, inert
        }

        [Fact]
        public void ToHtml_MarkdownSpans_StillConvert()
        {
            var html = MarkdownHelper.ToHtml("see <markdown>**bold** [link](https://example.com)</markdown> now").ToString();

            Assert.Contains("<strong>bold</strong>", html, StringComparison.Ordinal);
            Assert.Contains("href=\"https://example.com\"", html, StringComparison.Ordinal);
            Assert.Contains("see", html, StringComparison.Ordinal);
            Assert.Contains("now", html, StringComparison.Ordinal);
        }

        [Fact]
        public void ToHtml_PlainAngleBracket_IsEscaped()
        {
            var html = MarkdownHelper.ToHtml("value < 5").ToString();

            Assert.DoesNotContain("< 5", html, StringComparison.Ordinal);
            Assert.Contains("&lt; 5", html, StringComparison.Ordinal);
        }
    }
}
