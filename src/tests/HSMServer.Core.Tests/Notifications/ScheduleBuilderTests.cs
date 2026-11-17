using System;
using System.Collections.Generic;
using System.Linq;
using HSMServer.Core.Model.Policies;
using HSMServer.Notifications.AddressBook;
using Xunit;

namespace HSMServer.Core.Tests.Notifications
{
    // Scheduled alerts of a chat are buffered by repeat mode and by time slot, and the periodic
    // flush turns every repeat mode into one report. An alert that arrives while the reports are
    // being built must be in exactly one report, of this flush or of the next one (#1778).
    public class ScheduleBuilderTests
    {
        private const string Template = "$comment";

        private static readonly DateTime Day = new(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc);


        // The flush reads the 10:00 slot, then the 11:00 slot. The late alert arrives while the
        // 11:00 slot is being read and belongs to:
        [Theory]
        [InlineData(10)] // a slot the flush has finished with
        [InlineData(11)] // the slot the flush is reading
        [InlineData(12)] // a slot that did not exist when the flush started
        public void AddMessage_WhileTheReportOfItsModeIsBeingBuilt_AlertIsInExactlyOneReport(int lateAlertHour)
        {
            var builder = new ScheduleBuilder();
            var flushing = false;
            var lateAlertAddedDuringFlush = false;

            void AddLateAlert()
            {
                lateAlertAddedDuringFlush = flushing;
                builder.AddMessage(BuildAlert(AlertRepeatMode.Hourly, lateAlertHour, "late alert"));
            }

            builder.AddMessage(BuildAlert(AlertRepeatMode.Hourly, hour: 10, "ten o'clock"));
            builder.AddMessage(BuildAlert(AlertRepeatMode.Hourly, hour: 11, "eleven o'clock", grouping: AddLateAlert));

            flushing = true;

            var reports = string.Concat(builder.GetReports().ToList().Concat(builder.GetReports().ToList()));

            Assert.True(lateAlertAddedDuringFlush);
            Assert.Equal(1, CountOf("late alert", reports));
            Assert.Equal(1, CountOf("ten o'clock", reports));
            Assert.Equal(1, CountOf("eleven o'clock", reports));
            Assert.Empty(builder.GetReports());
        }

        [Fact]
        public void GetReports_TwoRepeatModes_EachModeGetsItsOwnReport()
        {
            var builder = new ScheduleBuilder();

            builder.AddMessage(BuildAlert(AlertRepeatMode.Hourly, hour: 10, "hourly alert"));
            builder.AddMessage(BuildAlert(AlertRepeatMode.Daily, hour: 10, "daily alert"));

            var reports = builder.GetReports().ToList();

            Assert.Equal(2, reports.Count);
            Assert.Single(reports, r => r.Contains("hourly alert") && r.Contains("10:00-11:00 (UTC)"));
            Assert.Single(reports, r => r.Contains("daily alert") && r.Contains("00:00-00:00 (UTC)"));
            Assert.Empty(builder.GetReports());
        }

        // The reports are produced one by one and each is sent before the next one is asked for,
        // so an alert can arrive between two reports of one flush: for the mode that has just
        // been reported, or for the one that is still waiting.
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AddMessage_BetweenTwoReportsOfOneFlush_AlertIsInExactlyOneReport(bool lateAlertModeIsAlreadyReported)
        {
            var builder = new ScheduleBuilder();

            builder.AddMessage(BuildAlert(AlertRepeatMode.Hourly, hour: 10, "hourly alert"));
            builder.AddMessage(BuildAlert(AlertRepeatMode.Daily, hour: 10, "daily alert"));

            var reports = new List<string>();

            using (var flush = builder.GetReports().GetEnumerator())
            {
                Assert.True(flush.MoveNext());
                reports.Add(flush.Current);

                var reportedMode = flush.Current.Contains("hourly alert") ? AlertRepeatMode.Hourly : AlertRepeatMode.Daily;
                var pendingMode = reportedMode is AlertRepeatMode.Hourly ? AlertRepeatMode.Daily : AlertRepeatMode.Hourly;

                builder.AddMessage(BuildAlert(lateAlertModeIsAlreadyReported ? reportedMode : pendingMode, hour: 10, "late alert"));

                while (flush.MoveNext())
                    reports.Add(flush.Current);
            }

            reports.AddRange(builder.GetReports().ToList());

            var allReports = string.Concat(reports);

            Assert.Equal(1, CountOf("late alert", allReports));
            Assert.Equal(1, CountOf("hourly alert", allReports));
            Assert.Equal(1, CountOf("daily alert", allReports));
            Assert.Empty(builder.GetReports());
        }

        // A flush that stops after a report (the bot is shutting down, a send was cancelled) has
        // not read the other repeat modes; their alerts wait for the next flush.
        [Fact]
        public void GetReports_FlushAbandonedAfterTheFirstReport_OtherModesAreReportedByTheNextFlush()
        {
            var builder = new ScheduleBuilder();

            builder.AddMessage(BuildAlert(AlertRepeatMode.Hourly, hour: 10, "hourly alert"));
            builder.AddMessage(BuildAlert(AlertRepeatMode.Daily, hour: 10, "daily alert"));

            var reports = new List<string> { builder.GetReports().First() };

            reports.AddRange(builder.GetReports().ToList());

            var allReports = string.Concat(reports);

            Assert.Equal(1, CountOf("hourly alert", allReports));
            Assert.Equal(1, CountOf("daily alert", allReports));
        }


        private static int CountOf(string text, string reports) => reports.Split(text).Length - 1;

        private static AlertResult BuildAlert(AlertRepeatMode mode, int hour, string comment, Action grouping = null)
        {
            var state = new DrainInterleavingState
            {
                Template = AlertState.BuildSystemTemplate(Template),
                Comment = comment,
                Grouping = grouping,
            };

            var alert = new AlertResult(new AlertDestination(), string.Empty, state.BuildComment(), Guid.NewGuid(), Template, state);

            return alert with { SchedulePeriod = mode, BuildDate = Day.AddHours(hour).AddMinutes(7) };
        }
    }
}
