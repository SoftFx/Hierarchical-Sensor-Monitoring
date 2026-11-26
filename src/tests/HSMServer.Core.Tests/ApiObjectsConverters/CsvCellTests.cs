using System;
using System.Collections.Generic;
using HSMCommon.Model;
using HSMServer.ApiObjectsConverters;
using Xunit;

namespace HSMServer.Core.Tests.ApiObjectsConverters
{
    // #1886: the history export writes a collector's free text (a name, an extension, a comment, a string value) as text:
    // a quote doubled, a text a spreadsheet would read as a formula prefixed, the bidirectional controls neutralized. A
    // typed value and a file's base64 content are written as they are.
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
        [InlineData("", "\"\"")]
        public void Free_text_is_written_as_text(string text, string expected)
        {
            Assert.Equal(expected, ApiCsvConverters.CsvTextCell(text));
        }

        [Fact]
        public void A_bidirectional_control_in_free_text_is_neutralized()
        {
            Assert.DoesNotContain('\u202E', ApiCsvConverters.CsvTextCell("invoice\u202Efdp.exe"));
        }

        [Fact]
        public void The_export_writes_a_collectors_name_and_comment_as_text()
        {
            var csv = Export(new FileValue
            {
                Time = Time, Name = "=cmd|' /C calc'!A0", Extension = "csv", Value = [], OriginalSize = 0, Comment = "say \"x\"", Status = SensorStatus.Ok,
            });

            Assert.Contains("\"'=cmd|' /C calc'!A0\"", csv);
            Assert.Contains("\"say \"\"x\"\"\"", csv);
        }

        [Fact]
        public void A_files_content_that_starts_with_a_plus_in_base64_is_written_as_it_is()
        {
            byte[] content = [0xF8, 0x01, 0x02];
            var base64 = Convert.ToBase64String(content);
            Assert.StartsWith("+", base64);

            var csv = Export(new FileValue
            {
                Time = Time, Name = "dump", Extension = "bin", Value = content, OriginalSize = content.Length, Status = SensorStatus.Ok,
            });

            Assert.Contains($"\"{base64}\"", csv);
            Assert.DoesNotContain($"'{base64}", csv);
        }

        [Fact]
        public void A_negative_timespan_and_a_negative_number_are_written_as_they_are()
        {
            var timespan = Export(new TimeSpanValue { Time = Time, Value = TimeSpan.FromMinutes(-5), Status = SensorStatus.Ok });
            var number = Export(new IntegerValue { Time = Time, Value = -5, Status = SensorStatus.Ok });

            Assert.DoesNotContain("'-", timespan);
            Assert.Contains("\"-5\"", number);
        }

        [Fact]
        public void A_string_value_is_free_text()
        {
            var csv = Export(new StringValue { Time = Time, Value = "=1+1", Status = SensorStatus.Ok });

            Assert.Contains("\"'=1+1\"", csv);
        }


        private static readonly DateTime Time = new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);

        private static string Export(BaseValue value) => new List<BaseValue> { value }.ConvertToCsv();
    }
}
