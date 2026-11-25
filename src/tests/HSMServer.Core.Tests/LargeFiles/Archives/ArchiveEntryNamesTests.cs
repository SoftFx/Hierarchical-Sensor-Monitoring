using HSMServer.Core.LargeFiles.Archives;
using Xunit;

namespace HSMServer.Core.Tests.LargeFiles.Archives
{
    // #1885: the entry name of an archive is a bare file name, whatever the collector sent.
    public sealed class ArchiveEntryNamesTests
    {
        [Theory]
        [InlineData("dump.dmp", "dmp", "dump.dmp")]
        [InlineData("dump", "dmp", "dump.dmp")]
        [InlineData("dump", ".dmp", "dump.dmp")]
        [InlineData("dump", null, "dump")]
        [InlineData("dump", "", "dump")]
        [InlineData("dump.dmp", "zip", "dump.dmp")]
        [InlineData("../../etc/passwd", null, "passwd")]
        [InlineData("..\\..\\Windows\\System32\\config", "bin", "config.bin")]
        [InlineData("C:\\dumps\\app.dmp", null, "app.dmp")]
        [InlineData("C:app.dmp", null, "app.dmp")]
        [InlineData("/var/crash/core.1234", null, "core.1234")]
        [InlineData("dumps/", "dmp", "file.dmp")]
        [InlineData("..", "dmp", "file.dmp")]
        [InlineData(".", null, "file")]
        [InlineData("...", null, "file")]
        [InlineData("", null, "file")]
        [InlineData(null, null, "file")]
        [InlineData("   ", "dmp", "file.dmp")]
        [InlineData("a\0b\r\nc.dmp", null, "abc.dmp")]
        [InlineData("evil\u202Egnp.exe", null, "evilgnp.exe")]
        [InlineData("zero\u200Bwidth.txt", null, "zerowidth.txt")]
        [InlineData("what?<is>|this*\".dmp", null, "what__is__this__.dmp")]
        [InlineData("file.dmp:stream", null, "file.dmp_stream")]
        [InlineData("trailing. . .", "dmp", "trailing.dmp")]
        [InlineData("CON", "txt", "_CON.txt")]
        [InlineData("nul.dmp", null, "_nul.dmp")]
        [InlineData("console.dmp", null, "console.dmp")]
        [InlineData(".bashrc", null, ".bashrc")]
        [InlineData("dump", "../../x", "dump.x")]
        [InlineData("dump", "a b", "dump")]
        [InlineData("дамп процесса.dmp", null, "дамп процесса.dmp")]
        public void Sanitize_MakesABareSafeName(string name, string extension, string expected)
        {
            Assert.Equal(expected, ArchiveEntryNames.Sanitize(name, extension));
        }

        [Fact]
        public void LongName_IsCutToTheLimit_KeepingTheExtension()
        {
            var result = ArchiveEntryNames.Sanitize(new string('a', 500), "dmp");

            Assert.Equal(ArchiveEntryNames.MaxLength, result.Length);
            Assert.EndsWith("a.dmp", result);
        }

        [Fact]
        public void LongName_IsNotCutInsideASurrogatePair()
        {
            var name = new string('a', ArchiveEntryNames.MaxLength - 5) + string.Concat(System.Linq.Enumerable.Repeat("\U0001F600", 10)) + ".dmp";

            var result = ArchiveEntryNames.Sanitize(name, null);

            Assert.True(result.Length <= ArchiveEntryNames.MaxLength);
            Assert.EndsWith(".dmp", result);
            Assert.False(char.IsHighSurrogate(result[^5]), "a lone high surrogate before the extension");
        }
    }
}
