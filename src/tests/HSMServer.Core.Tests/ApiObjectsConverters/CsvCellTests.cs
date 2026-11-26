using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HSMCommon.Model;
using HSMServer.ApiObjectsConverters;
using Xunit;

namespace HSMServer.Core.Tests.ApiObjectsConverters
{
    // #1886: an export of sensor values is written for its reader. A script (the Sensor API) gets every value as it is
    // stored, quoted, a quote doubled. A person who opens the web UI's download in a spreadsheet gets a collector's free
    // text (a name, an extension, a comment, a string value) as text: a text a spreadsheet would read as a formula
    // prefixed, the bidirectional controls neutralized. A typed value and a file's base64 content are written as they are
    // for both.
    public class CsvCellTests
    {
        private static readonly string Bidi = ((char)0x202E).ToString();
        private static readonly string Separator = CultureInfo.CurrentUICulture.TextInfo.ListSeparator;


        [Theory]
        [InlineData("report", "\"report\"")]
        [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
        [InlineData("=HYPERLINK(\"x\")", "\"'=HYPERLINK(\"\"x\"\")\"")]
        [InlineData("+cmd", "\"'+cmd\"")]
        [InlineData("-cmd", "\"'-cmd\"")]
        [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
        [InlineData("\tx", "\"'\tx\"")]
        [InlineData("", "\"\"")]
        public void Free_text_for_a_spreadsheet_is_written_as_text(string text, string expected)
        {
            Assert.Equal(expected, ApiCsvConverters.CsvTextCell(text));
        }

        [Fact]
        public void A_bidirectional_control_in_free_text_for_a_spreadsheet_is_neutralized()
        {
            // The char overload: a culture comparison of strings ignores U+202E and would find it anywhere.
            Assert.DoesNotContain((char)0x202E, ApiCsvConverters.CsvTextCell("invoice" + Bidi + "fdp.exe"));
        }

        // The cells of a script's export are the stored values, each quoted as before #1886, the quote now doubled: a
        // String value -5 stays -5, =1+1 stays =1+1, a comment keeps its bidirectional control.
        [Fact]
        public void A_script_gets_string_values_and_comments_as_they_are_stored()
        {
            var csv = Export(CsvReader.Script, Strings());
            var lines = Lines(csv);

            Assert.Equal(string.Join(Separator, "Date", "Receiving time", "Value", "Status", "Comment"), lines[0]);
            Assert.Equal(["\"=1+1\"", "\"Ok\"", "\"say \"\"x\"\"\""], Cells(lines[1])[2..]);
            Assert.Equal(["\"-5\"", "\"Ok\"", "\"a" + Bidi + "b\""], Cells(lines[2])[2..]);
            Assert.DoesNotContain("'", csv);
        }

        [Fact]
        public void A_spreadsheet_gets_string_values_and_comments_as_text()
        {
            var csv = Export(CsvReader.Spreadsheet, Strings());
            var lines = Lines(csv);

            Assert.Equal(["\"'=1+1\"", "\"Ok\"", "\"say \"\"x\"\"\""], Cells(lines[1])[2..]);
            Assert.Equal(["\"'-5\"", "\"Ok\"", "\"a" + (char)0xFFFD + "b\""], Cells(lines[2])[2..]);
            Assert.DoesNotContain((char)0x202E, csv);
        }

        // Only the free-text cells differ between the two readers: the times and the status are the same cells.
        [Fact]
        public void The_two_readers_differ_only_in_free_text()
        {
            var script = Lines(Export(CsvReader.Script, Strings()));
            var spreadsheet = Lines(Export(CsvReader.Spreadsheet, Strings()));

            Assert.Equal(script[0], spreadsheet[0]);
            Assert.Equal(Cells(script[1])[..2], Cells(spreadsheet[1])[..2]);
            Assert.Equal(Cells(script[2])[..2], Cells(spreadsheet[2])[..2]);
        }

        [Fact]
        public void A_name_and_a_comment_of_a_file_are_text_only_for_a_spreadsheet()
        {
            var value = new FileValue
            {
                Time = Time, Name = "=SUM(1,2)", Extension = "csv", Value = [], OriginalSize = 0, Comment = "-x", Status = SensorStatus.Ok,
            };

            var spreadsheet = Export(CsvReader.Spreadsheet, value);
            Assert.Contains("\"'=SUM(1,2)\"", spreadsheet);
            Assert.Contains("\"'-x\"", spreadsheet);

            var script = Export(CsvReader.Script, value);
            Assert.Contains("\"=SUM(1,2)\"", script);
            Assert.Contains("\"-x\"", script);
            Assert.DoesNotContain("'=", script);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_files_content_that_starts_with_a_plus_in_base64_is_written_as_it_is(bool forSpreadsheet)
        {
            byte[] content = [0xF8, 0x01, 0x02];
            var base64 = Convert.ToBase64String(content);
            Assert.StartsWith("+", base64);

            var csv = Export(Reader(forSpreadsheet), new FileValue
            {
                Time = Time, Name = "dump", Extension = "bin", Value = content, OriginalSize = content.Length, Status = SensorStatus.Ok,
            });

            Assert.Contains($"\"{base64}\"", csv);
            Assert.DoesNotContain($"'{base64}", csv);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_negative_timespan_and_a_negative_number_are_written_as_they_are(bool forSpreadsheet)
        {
            var reader = Reader(forSpreadsheet);
            var timespan = Export(reader, new TimeSpanValue { Time = Time, Value = TimeSpan.FromMinutes(-5), Status = SensorStatus.Ok });
            var number = Export(reader, new IntegerValue { Time = Time, Value = -5, Status = SensorStatus.Ok });

            Assert.DoesNotContain("'-", timespan);
            Assert.Contains("\"-5\"", number);
            Assert.DoesNotContain("'", number);
        }


        private static readonly DateTime Time = new(2026, 11, 26, 0, 0, 0, DateTimeKind.Utc);

        private static BaseValue[] Strings() =>
        [
            new StringValue { Time = Time, ReceivingTime = Time, Value = "-5", Comment = "a" + Bidi + "b", Status = SensorStatus.Ok },
            new StringValue { Time = Time.AddSeconds(1), ReceivingTime = Time.AddSeconds(1), Value = "=1+1", Comment = "say \"x\"", Status = SensorStatus.Ok },
        ];

        private static CsvReader Reader(bool forSpreadsheet) => forSpreadsheet ? CsvReader.Spreadsheet : CsvReader.Script;

        private static string Export(CsvReader reader, params BaseValue[] values) => new List<BaseValue>(values).ConvertToCsv(reader);

        private static string[] Lines(string csv) => csv.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        // The cells of a row whose cells hold no separator.
        private static string[] Cells(string line) => line.Split(Separator);
    }
}
