using System;
using System.Collections.Generic;
using HSMCommon.Model;
using HSMServer.ApiObjectsConverters;
using Xunit;

namespace HSMServer.Core.Tests.ApiObjectsConverters
{
    // #1886: the history export writes every cell as text: a quote doubled, a text a spreadsheet would read as a formula
    // prefixed, a number left a number, the bidirectional controls neutralized.
    public class CsvCellTests
    {
        [Theory]
        [InlineData("report", "\"report\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
        [InlineData("+cmd", "\"'+cmd\"")]
        [InlineData("-cmd", "\"'-cmd\"")]
        [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
        [InlineData("\tx", "\"'\tx\"")]
        [InlineData("-5", "\"-5\"")]
        [InlineData("-1.5E3", "\"-1.5E3\"")]
        [InlineData("+7", "\"+7\"")]
        [InlineData("", "\"\"")]
        public void A_cell_is_written_as_text(string text, string expected)
        {
            Assert.Equal(expected, ApiCsvConverters.CsvCell(text));
        }

        [Fact]
        public void A_bidirectional_control_is_neutralized()
        {
            Assert.DoesNotContain('‮', ApiCsvConverters.CsvCell("invoice‮fdp.exe"));
        }

        [Fact]
        public void The_export_writes_a_collectors_name_and_comment_as_text()
        {
            var csv = new List<BaseValue>
            {
                new FileValue
                {
                    Time = new DateTime(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc), Name = "=cmd|' /C calc'!A0", Extension = "csv",
                    Value = [], OriginalSize = 0, Comment = "say \"x\"", Status = SensorStatus.Ok,
                },
            }.ConvertToCsv();

            Assert.Contains("\"'=cmd|' /C calc'!A0\"", csv);
            Assert.Contains("\"say \"\"x\"\"\"", csv);
        }
    }
}
