using System;
using System.Collections.Generic;
using System.Linq;
using HSMDatabase.AccessManager.DatabaseEntities;


namespace HSMServer.Core.Model.Policies
{
    public class AlertSchedule
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; }
        public string Timezone { get; set; }
        public string Schedule { get; set; }

        public List<DaySchedule> DaySchedules { get; set; } = new();
        public List<DateTime> DisabledDates { get; set; } = new();
        public Overrides Overrides { get; set; } = new();

        public bool IsWorkingTime(DateTime dateTime)
        {
            var localDateTime = ConvertUtcToLocalTime(dateTime);
            var date = localDateTime.Date;
            var timeOfDay = localDateTime.TimeOfDay;

            var customOverride = Overrides.CustomScheduleDates
                .FirstOrDefault(c => c.Date == date);

            if (customOverride != null)
            {
                if (customOverride.Windows?.Any() == true)
                {
                    return customOverride.Windows.Any(w =>
                        timeOfDay >= w.Start && timeOfDay < w.End);
                }

                if (!string.IsNullOrEmpty(customOverride.ScheduleType))
                {
                    var scheduleForType = GetScheduleByType(customOverride.ScheduleType);
                    if (scheduleForType != null)
                    {
                        return scheduleForType.Windows.Any(w =>
                            timeOfDay >= w.Start && timeOfDay < w.End);
                    }
                }
            }

            if (Overrides.EnabledDates.Contains(date))
            {
                var daySchedule = GetDaySchedule(date.DayOfWeek);
                return daySchedule?.Windows.Any(w =>
                    timeOfDay >= w.Start && timeOfDay < w.End) ?? false;
            }

            if (DisabledDates.Contains(date))
                return false;

            var regularSchedule = GetDaySchedule(date.DayOfWeek);
            return regularSchedule?.Windows.Any(w =>
                timeOfDay >= w.Start && timeOfDay < w.End) ?? false;
        }

        private DateTime ConvertUtcToLocalTime(DateTime utcDateTime)
        {
            var timezone = TimeZoneInfo.FindSystemTimeZoneById(Timezone);

            return TimeZoneInfo.ConvertTimeFromUtc(utcDateTime, timezone);
        }

        private DaySchedule GetDaySchedule(DayOfWeek dayOfWeek)
        {
            return DaySchedules?
                .FirstOrDefault(ds => ds.Days.Contains(dayOfWeek));
        }

        private DaySchedule GetScheduleByType(string scheduleType)
        {
            return DaySchedules?
                .FirstOrDefault(ds =>
                    ds.Id == scheduleType ||
                    ds.Days.Any(d => d.ToString().StartsWith(scheduleType)));
        }

        public List<DateTime> GetWorkingHoursForDate(DateTime date, TimeSpan interval = default)
        {
            if (interval == default)
                interval = TimeSpan.FromHours(1);

            var result = new List<DateTime>();
            var windows = GetWorkingWindowsForDate(date);

            foreach (var window in windows)
            {
                var currentTime = date.Date.Add(window.Start);
                var endTime = date.Date.Add(window.End);

                while (currentTime < endTime)
                {
                    result.Add(currentTime);
                    currentTime = currentTime.Add(interval);
                }
            }

            return result;
        }

        private List<TimeWindow> GetWorkingWindowsForDate(DateTime date)
        {
            var customOverride = Overrides.CustomScheduleDates
                .FirstOrDefault(c => c.Date == date.Date);

            if (customOverride != null)
            {
                if (customOverride.Windows?.Any() == true)
                    return customOverride.Windows;

                if (!string.IsNullOrEmpty(customOverride.ScheduleType))
                {
                    var schedule = GetScheduleByType(customOverride.ScheduleType);
                    if (schedule != null)
                        return schedule.Windows;
                }
            }

            if (Overrides.EnabledDates.Contains(date.Date))
            {
                var schedule = GetDaySchedule(date.DayOfWeek);
                return schedule?.Windows ?? new List<TimeWindow>();
            }

            if (DisabledDates.Contains(date.Date))
                return new List<TimeWindow>();

            var regularSchedule = GetDaySchedule(date.DayOfWeek);
            return regularSchedule?.Windows ?? new List<TimeWindow>();
        }

        public AlertScheduleEntity ToEntity()
        {
            return new()
            {
                Id = Id.ToByteArray(),
                Name = Name,
                Timezone = Timezone,
                Schedule = Schedule,
            };
        }
    }
    public class DaySchedule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public List<DayOfWeek> Days { get; set; } = new();
        public List<TimeWindow> Windows { get; set; } = new();
    }

    public class TimeWindow
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }

        public TimeWindow() { }

        public TimeWindow(string start, string end)
        {
            Start = TimeSpan.Parse(start);
            End = TimeSpan.Parse(end);
        }

        public TimeWindow(TimeSpan start, TimeSpan end)
        {
            Start = start;
            End = end;
        }

        public override string ToString()
        {
            return $"{Start:hh\\:mm} - {End:hh\\:mm}";
        }
    }

    public class Overrides
    {
        public List<DateTime> EnabledDates { get; set; } = new();

        public List<CustomScheduleDate> CustomScheduleDates { get; set; } = new();
    }

    public class CustomScheduleDate
    {
        public DateTime Date { get; set; }

        public string ScheduleType { get; set; }

        public List<TimeWindow> Windows { get; set; }

        public CustomScheduleDate() { }

        public CustomScheduleDate(DateTime date, string scheduleType)
        {
            Date = date;
            ScheduleType = scheduleType;
        }

        public CustomScheduleDate(DateTime date, List<TimeWindow> windows)
        {
            Date = date;
            Windows = windows;
        }
    }
}
