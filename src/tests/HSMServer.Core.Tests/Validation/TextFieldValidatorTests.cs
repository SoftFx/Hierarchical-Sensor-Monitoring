using System;
using HSMServer.Validation;
using Xunit;

namespace HSMServer.Core.Tests.Validation
{
    // #1705: the characters no legitimate icon, message template or name
    // carries. Tab and newline stay allowed (a message template may be
    // multi-line); emoji and the soft direction marks are legitimate text.
    public class TextFieldValidatorTests
    {
        [Theory]
        [InlineData("plain text")]
        [InlineData("multi\nline\ttext")]
        [InlineData("emoji \U0001F525 and unicode")]
        [InlineData("soft lrm \u200e")]
        [InlineData("line separator \u2028")]
        public void LegitimateTextPasses(string text)
        {
            Assert.False(TextFieldValidator.HasForbiddenCharacter(text, out _));
        }

        [Theory]
        [InlineData("nul \0")]
        [InlineData("carriage return\r")]
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

        [Fact]
        public void EmptyTextPasses()
        {
            Assert.False(TextFieldValidator.HasForbiddenCharacter(null, out _));
            Assert.False(TextFieldValidator.HasForbiddenCharacter(string.Empty, out _));
        }
    }
}
