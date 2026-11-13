using System;
using HSMServer.Validation;
using Xunit;

namespace HSMServer.Core.Tests.Validation
{
    // #1705: the characters no legitimate icon, message template or name
    // carries. Tab, newline and carriage return stay allowed in message
    // templates (they may be multi-line, and a Windows CRLF paste must not
    // fail; \r is inert in both renderers); emoji and the soft direction
    // marks are legitimate text. Names and icons are SINGLE-LINE surfaces:
    // there the line-break characters are forbidden too (a newline in a name
    // breaks single-line rendering in lists, dropdowns and Telegram headers).
    public class TextFieldValidatorTests
    {
        [Theory]
        [InlineData("plain text")]
        [InlineData("multi\nline\ttext")]
        [InlineData("windows\r\npaste")]
        [InlineData("bare\rcarriage return")]
        [InlineData("emoji \U0001F525 and unicode")]
        [InlineData("soft lrm \u200e")]
        [InlineData("line separator \u2028")]
        public void LegitimateTextPasses(string text)
        {
            Assert.False(TextFieldValidator.HasForbiddenCharacter(text, out _));
        }

        [Theory]
        [InlineData("nul \0")]
        [InlineData("escape \u001b[0m")]
        [InlineData("delete \u007f")]
        [InlineData("c1 \u0085")]
        [InlineData("rtl override \u202e")]
        [InlineData("lri isolate \u2066")]
        public void ForbiddenCharactersAreNamed(string text)
        {
            Assert.True(TextFieldValidator.HasForbiddenCharacter(text, out var found));
            Assert.StartsWith("U+", found, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("prod\nalerts")]
        [InlineData("prod\r\nalerts")]
        [InlineData("tabbed\tname")]
        public void SingleLineSurfaces_ForbidLineBreaksToo(string name)
        {
            // Names and icons render on one line (lists, dropdowns, Telegram
            // headers) — the line-break characters are forbidden there even
            // though templates allow them (#1705 review).
            Assert.True(TextFieldValidator.HasForbiddenCharacter(name, out var found, allowLineBreaks: false));
            Assert.StartsWith("U+", found, StringComparison.Ordinal);
        }

        [Fact]
        public void SingleLineSurfaces_StillAcceptPlainAndEmojiText()
        {
            Assert.False(TextFieldValidator.HasForbiddenCharacter("prod alerts \U0001F525", out _, allowLineBreaks: false));
        }

        [Fact]
        public void EmptyTextPasses()
        {
            Assert.False(TextFieldValidator.HasForbiddenCharacter(null, out _));
            Assert.False(TextFieldValidator.HasForbiddenCharacter(string.Empty, out _));
        }
    }
}
