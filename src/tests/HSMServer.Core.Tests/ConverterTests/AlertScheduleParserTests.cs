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

    }
}
