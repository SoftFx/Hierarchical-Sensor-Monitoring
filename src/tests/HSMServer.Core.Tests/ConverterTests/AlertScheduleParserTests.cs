using HSMServer.Core.Schedule;
using System;
using Xunit;


namespace HSMServer.Core.Tests.ConverterTests
{
    public class AlertScheduleParserTests()
    {

        private readonly AlertScheduleParser _parser = new AlertScheduleParser();


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


        // #1598: the write contract makes the daySchedules key required, but
        // the model's `new()` initializer hides the difference between an
        // absent key and an explicit empty list — HasDaySchedulesKey is the
        // write path's presence probe.
        [Theory]
        [InlineData("daySchedules:\n  - days: [Mon]\n    windows:\n      - { start: \"09:00\", end: \"18:00\" }\n", true)]
        [InlineData("daySchedules: []\n", true)]
        [InlineData("daySchedules:\n", false)] // explicit null value, not a list
        [InlineData("disabledDates: [\"2026-12-25\"]\n", false)]
        [InlineData("daySchedule:\n  - days: [Mon]\n    windows:\n      - { start: \"09:00\", end: \"18:00\" }\n", false)] // key typo
        [InlineData("", false)]
        [InlineData(null, false)]
        public void HasDaySchedulesKeyDistinguishesAbsentKeyFromEmptyList(string yaml, bool expected)
        {
            Assert.Equal(expected, _parser.HasDaySchedulesKey(yaml));
        }


        // The load path keeps accepting a missing daySchedules key: rows
        // stored before the write-path rule must keep parsing (#1598).
        [Fact]
        public void ParseScheduleWithoutDaySchedulesKeyStillLoads()
        {
            var yaml = "disabledDates: [\"2026-12-25\"]\n";

            var result = _parser.Parse(yaml);

            Assert.NotNull(result);
            Assert.Empty(result.DaySchedules);
            Assert.Equal(yaml, result.Schedule);
        }

    }
}
