using HSMDataCollector.LargeFiles;
using Xunit;

namespace HSMDataCollector.Tests.LargeFiles
{
    /// <summary>The mask rule both collectors share (#1888 design §12.10).</summary>
    public sealed class FileNameMaskTests
    {
        [Theory]
        [InlineData("*.dmp", "a.dmp", true)]
        [InlineData("*.dmp", ".dmp", true)]
        [InlineData("*.dmp", "a.dmp_old", false)]
        [InlineData("*.dmp", "a.dmpx", false)]
        [InlineData("*.dmp", "a.dm", false)]
        [InlineData("app_*.dmp", "app_20261125.dmp", true)]
        [InlineData("app_*.dmp", "other_1.dmp", false)]
        [InlineData("a?c", "abc", true)]
        [InlineData("a?c", "ac", false)]
        [InlineData("a?c", "abbc", false)]
        [InlineData("*", "anything", true)]
        [InlineData("*", "", true)]
        [InlineData("**a", "bba", true)]
        [InlineData("*a*b*", "xaybz", true)]
        [InlineData("*a*b*", "xbya", false)]
        [InlineData("exact.dmp", "exact.dmp", true)]
        [InlineData("exact.dmp", "exact.dmp.1", false)]
        public void Matches_the_whole_name(string mask, string name, bool expected)
        {
            Assert.Equal(expected, new FileNameMask(mask, false).Matches(name));
            Assert.Equal(expected, new FileNameMask(mask, true).Matches(name));
        }

        [Theory]
        [InlineData("*.DMP", "a.dmp", true, false)]
        [InlineData("A?C", "abc", true, false)]
        [InlineData("*.dmp", "A.DMP", true, false)]
        public void Case_folds_ascii_only_where_asked(string mask, string name, bool insensitive, bool sensitive)
        {
            Assert.Equal(insensitive, new FileNameMask(mask, true).Matches(name));
            Assert.Equal(sensitive, new FileNameMask(mask, false).Matches(name));
        }

        [Fact]
        public void Does_not_fold_letters_outside_ascii()
        {
            Assert.False(new FileNameMask("Ä.dmp", true).Matches("ä.dmp"));
        }
    }
}
