using HSMServer.Core.Schedule;
using System;
using System.Text.RegularExpressions;
using Xunit;


namespace HSMServer.Core.Tests.ConverterTests
{
    public class AlertScheduleParserTests()
    {

        private readonly AlertScheduleParser _parser = new();

        // A server-generated Guid must never appear in a validation message:
        // the client cannot map it back to anything it sent (#1599).
        private static readonly Regex GuidPattern =
            new("[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}", RegexOptions.Compiled);


        [Fact]
        public void ParseValidScheduleTest()
        {
            var yaml = @"
daySchedules:
    - days: [Mon, Tue, Wed, Thu, Fri]
      windows:
        - { start: ""09:00"", end: ""11:30"" }
        - { start: ""12:30"", end: ""15:00"" }

    - days: [Sat]
      windows:
        - { start: ""10:00"", end: ""14:00"" }

disabledDates: [""2026-02-11"", ""2026-02-23""]

overrides:
    enabledDates: [""2026-03-20""]

    customScheduleDates:
        - date: ""2026-03-21""
          scheduleType: ""Sat""

        - date: ""2026-03-22""
          scheduleType: ""Mon""

        - date: ""2026-03-23""
          windows:
            - { start: ""11:00"", end: ""16:00"" }";

            var result = _parser.Parse(yaml);

            Assert.NotNull(result);
            Assert.Equal(2, result.DaySchedules.Count);
            Assert.Equal(2, result.DaySchedules[0].Windows.Count);
            Assert.Equal(2, result.DisabledDates.Count);
            Assert.Single(result.Overrides.EnabledDates);
            Assert.Equal(3, result.Overrides.CustomScheduleDates.Count);
        }


        [Fact]
        public void ParseStartAfterEndThrowsValidationException()
        {
            var yaml = @"daySchedules:
      - days: [Mon, Tue, Wed, Thu, Fri]
        windows:
            - { start: ""12:00"", end: ""11:30"" }
                        ";

            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));
            Assert.Contains("must be less", exception.Message);
        }


        [Fact]
        public void ParseInvalidDateThrowsValidationException()
        {
            var yaml = @"daySchedules:
      - days: [Mon, Tue, Wed, Thu, Fri]
        windows:
            - { start: ""12:00"", end: ""11:30"" }

disabledDates: [""2026-13-33""]
                        ";

            Assert.Throws<Exception>(() => _parser.Parse(yaml));
        }


        [Theory]
        [InlineData("25:00", "17:00")]
        [InlineData("09:00", "25:00")]
        [InlineData("abc", "17:00")]
        public void ParseInvalidTimeThrowsValidationException(string start, string end)
        {
            var yaml = $@"daySchedules:
                        - days: [Mon, Tue, Wed]
                          windows:
                            - {{ start: ""{start}"", end: ""{end}"" }}
                       ";

            Assert.Throws<Exception>(() => _parser.Parse(yaml));
        }


        [Fact]
        public void ParseEndOfDayWindowUsesOneDayFormTest()
        {
            var yaml = @"daySchedules:
      - days: [Sat]
        windows:
            - { start: ""22:00"", end: ""1.00:00"" }
                        ";

            var result = _parser.Parse(yaml);

            Assert.NotNull(result);
            var window = Assert.Single(result.DaySchedules[0].Windows);
            Assert.Equal(TimeSpan.FromDays(1), window.End);
        }


        [Fact]
        public void ParseTwentyFourHoursEndThrowsValidationException()
        {
            var yaml = @"daySchedules:
      - days: [Sat]
        windows:
            - { start: ""22:00"", end: ""24:00"" }
                        ";

            Assert.Throws<Exception>(() => _parser.Parse(yaml));
        }


        // === #1599: deterministic, field-specific validation messages ===
        // Every case pins the exact message the 422 details carry on the
        // `schedule` key: the offending value and the expected format, no
        // raw .NET exception text, no server-generated Guids.

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("# only a comment\n")]
        public void ParseEmptyScheduleThrowsExplicitEmptyError(string yaml)
        {
            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));

            Assert.Equal("YAML parsing error: The schedule is empty: provide a YAML object with a daySchedules key.",
                exception.Message);
            Assert.DoesNotContain("Object reference not set", exception.Message);
        }

        [Theory]
        [InlineData("09:00", "24:00", "24:00")]
        [InlineData("25:00", "17:00", "25:00")]
        [InlineData("09:00", "abc", "abc")]
        public void ParseInvalidTimeThrowsMessageWithOffendingValueAndFormat(string start, string end, string offending)
        {
            var yaml = $@"daySchedules:
      - days: [Mon]
        windows:
            - {{ start: ""{start}"", end: ""{end}"" }}
                        ";

            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));

            Assert.Equal($"YAML parsing error: Invalid time '{offending}' (expected HH:mm, or 1.00:00 for the end of the day)",
                exception.Message);
            Assert.DoesNotContain("Exception during deserialization", exception.Message);
        }

        [Fact]
        public void ParseUnknownDayThrowsMessageWithOffendingValueAndFormat()
        {
            var yaml = @"daySchedules:
      - days: [Mon, Funday]
        windows:
            - { start: ""09:00"", end: ""10:00"" }
                        ";

            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));

            Assert.Equal("YAML parsing error: Unknown day 'Funday' (expected mon..sun, 3-letter or full English day name)",
                exception.Message);
            Assert.DoesNotContain("Exception during deserialization", exception.Message);
            Assert.DoesNotContain("Requested value", exception.Message);
        }

        [Fact]
        public void ParseInvalidDisabledDateThrowsMessageWithOffendingValueAndFormat()
        {
            var yaml = @"daySchedules:
      - days: [Mon]
        windows:
            - { start: ""09:00"", end: ""10:00"" }

disabledDates: [""26-12-2026""]
                        ";

            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));

            Assert.Equal("YAML parsing error: Invalid date '26-12-2026' (expected yyyy-MM-dd)", exception.Message);
            Assert.DoesNotContain("Exception during deserialization", exception.Message);
            Assert.DoesNotContain("was not recognized as a valid DateTime", exception.Message);
        }

        [Fact]
        public void ParseOverlappingWindowsWithoutIdNamePositionInsteadOfGuid()
        {
            var yaml = @"daySchedules:
      - days: [Mon]
        windows:
            - { start: ""09:00"", end: ""11:00"" }
            - { start: ""10:00"", end: ""12:00"" }
                        ";

            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));

            Assert.Equal("YAML parsing error: Invalid schedule data: " +
                         "Overlapping time windows in daySchedules[0]: 09:00:00-11:00:00 and 10:00:00-12:00:00",
                exception.Message);
            Assert.DoesNotMatch(GuidPattern, exception.Message);
        }

        [Fact]
        public void ParseOverlappingWindowsWithClientIdNameClientSuppliedId()
        {
            var yaml = @"daySchedules:
      - id: workdays
        days: [Mon]
        windows:
            - { start: ""09:00"", end: ""11:00"" }
            - { start: ""10:00"", end: ""12:00"" }
                        ";

            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));

            Assert.Equal("YAML parsing error: Invalid schedule data: " +
                         "Overlapping time windows in 'workdays': 09:00:00-11:00:00 and 10:00:00-12:00:00",
                exception.Message);
        }

        [Theory]
        [InlineData("days", "DaySchedule daySchedules[0] does not contain any days")]
        [InlineData("windows", "DaySchedule daySchedules[0] does not contain any time windows")]
        public void ParseEmptyDayScheduleEntryWithoutIdNamesPosition(string missingKey, string expectedError)
        {
            var days = missingKey == "days" ? "" : "days: [Mon]\n        ";
            var windows = missingKey == "windows" ? "" : "windows:\n            - { start: \"09:00\", end: \"10:00\" }";

            var yaml = $@"daySchedules:
      - {days}{windows}
                        ";

            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));

            Assert.Equal($"YAML parsing error: Invalid schedule data: {expectedError}", exception.Message);
            Assert.DoesNotMatch(GuidPattern, exception.Message);
        }

        [Fact]
        public void ParseReversedWindowReportsOnlyStartEndError()
        {
            var yaml = @"daySchedules:
      - days: [Mon]
        windows:
            - { start: ""18:00"", end: ""09:00"" }
                        ";

            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));

            Assert.Equal("YAML parsing error: Invalid schedule data: " +
                         "Start time 18:00:00 must be less than end time 09:00:00",
                exception.Message);
            Assert.DoesNotContain("too short", exception.Message);
        }

        [Fact]
        public void ParseShortWindowStillReportsMinimumDurationError()
        {
            var yaml = @"daySchedules:
      - days: [Mon]
        windows:
            - { start: ""09:00:30"", end: ""09:01:00"" }
                        ";

            var exception = Assert.Throws<Exception>(() => _parser.Parse(yaml));

            Assert.Equal("YAML parsing error: Invalid schedule data: Time window too short (minimum 1 minute)",
                exception.Message);
        }

        [Fact]
        public void ParseWithoutExplicitIdsAssignsIdsAfterValidation()
        {
            var yaml = @"daySchedules:
      - days: [Mon]
        windows:
            - { start: ""09:00"", end: ""10:00"" }
                        ";

            var result = _parser.Parse(yaml);

            Assert.NotNull(result);
            Assert.True(Guid.TryParse(result.DaySchedules[0].Id, out _),
                "an id-less entry must still get a generated id after validation");
        }

        [Fact]
        public void ValidateStringOverlappingWindowsWithoutIdReportsPosition()
        {
            var yaml = @"daySchedules:
      - days: [Mon]
        windows:
            - { start: ""09:00"", end: ""11:00"" }
            - { start: ""10:00"", end: ""12:00"" }
                        ";

            var valid = _parser.Validate(yaml, out var errors);

            Assert.False(valid);
            var error = Assert.Single(errors);
            Assert.Contains("Overlapping time windows in daySchedules[0]", error);
            Assert.DoesNotMatch(GuidPattern, error);
        }

    }
}
