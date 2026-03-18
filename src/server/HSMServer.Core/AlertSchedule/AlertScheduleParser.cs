using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Schedule
{
    public class AlertScheduleParser
    {
        private readonly IDeserializer _deserializer;
        private readonly ISerializer _serializer;
        private const string DateFormat = "yyyy-MM-dd";
        private const string TimeFormat = @"hh\:mm";
        private readonly Regex _timeRegex = new Regex(@"^([0-1][0-9]|2[0-3]):[0-5][0-9]$", RegexOptions.Compiled);

        public AlertScheduleParser()
        {
            _deserializer = new DeserializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new TimeSpanYamlConverter())
                .WithTypeConverter(new DayOfWeekYamlConverter())
                .IgnoreUnmatchedProperties()
                .Build();

            _serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .WithTypeConverter(new TimeSpanYamlConverter())
                .WithTypeConverter(new DayOfWeekYamlConverter())
                .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
                .Build();
        }


        public AlertSchedule Parse(AlertScheduleEntity entity)
        {
            var result = Parse(entity.Schedule);
            result.Id = new Guid(entity.Id);
            result.Schedule = entity.Schedule;
            result.Timezone = entity.Timezone;
            result.Name = entity.Name;
            return result;
        }

        public AlertSchedule Parse(string yaml)
        {
            try
            {
                var schedule = _deserializer.Deserialize<AlertSchedule>(yaml);

                schedule.Schedule = yaml;

                if (!Validate(schedule, out var errors))
                {
                    throw new Exception($"Invalid schedule data: {string.Join("; ", errors)}");
                }

                PostProcessSchedule(schedule);
                return schedule;
            }
            catch (Exception ex)
            {
                throw new Exception($"YAML parsing error: {ex.Message}", ex);
            }
        }

        public string Serialize(AlertSchedule schedule)
        {
            try
            {
                if (!Validate(schedule, out var errors))
                {
                    throw new Exception($"Invalid schedule data: {string.Join("; ", errors)}");
                }

                return _serializer.Serialize(schedule);
            }
            catch (Exception ex)
            {
                throw new Exception($"YAML serialization error: {ex.Message}", ex);
            }
        }

        public bool Validate(string yaml, out List<string> errors)
        {
            errors = new List<string>();
            try
            {
                var schedule = _deserializer.Deserialize<AlertSchedule>(yaml);
                return Validate(schedule, out errors);
            }
            catch (Exception ex)
            {
                errors.Add($"YAML parsing error: {ex.Message}");
                return false;
            }
        }

        private bool Validate(AlertSchedule schedule, out List<string> errors)
        {
            errors = new List<string>();

            if (schedule.DaySchedules != null)
            {
                foreach (var daySchedule in schedule.DaySchedules)
                {
                    ValidateDaySchedule(daySchedule, errors);
                }
            }
            else
            {
                errors.Add("DaySchedules collection is required");
            }

            if (schedule.DisabledDates != null)
            {
                foreach (var date in schedule.DisabledDates)
                {
                    ValidateDate(date, "DisabledDate", errors);
                }
            }

            if (schedule.Overrides != null)
            {
                ValidateOverrides(schedule.Overrides, errors);
            }

            return !errors.Any();
        }

        private void ValidateDaySchedule(DaySchedule daySchedule, List<string> errors)
        {
            if (daySchedule.Days == null || !daySchedule.Days.Any())
            {
                errors.Add($"DaySchedule '{daySchedule.Id}' does not contain any days");
            }

            if (daySchedule.Windows == null || !daySchedule.Windows.Any())
            {
                errors.Add($"DaySchedule '{daySchedule.Id}' does not contain any time windows");
            }
            else
            {
                foreach (var window in daySchedule.Windows)
                {
                    ValidateTimeWindow(window, daySchedule.Id, errors);
                }

                CheckForOverlappingWindows(daySchedule.Windows, daySchedule.Id, errors);
            }
        }

        private void ValidateTimeWindow(TimeWindow window, string scheduleId, List<string> errors)
        {
            if (window.Start < TimeSpan.Zero || window.Start >= TimeSpan.FromDays(1))
            {
                errors.Add($"Invalid start time {window.Start}");
            }

            if (window.End < TimeSpan.Zero || window.End > TimeSpan.FromDays(1))
            {
                errors.Add($"Invalid end time {window.End}");
            }

            if (window.Start >= window.End)
            {
                errors.Add($"Start time {window.Start} must be less than end time {window.End}");
            }

            if (window.End - window.Start < TimeSpan.FromMinutes(1))
            {
                errors.Add($"Time window too short (minimum 1 minute)");
            }
        }

        private void CheckForOverlappingWindows(List<TimeWindow> windows, string scheduleId, List<string> errors)
        {
            var sortedWindows = windows.OrderBy(w => w.Start).ToList();

            for (int i = 0; i < sortedWindows.Count - 1; i++)
            {
                if (sortedWindows[i].End > sortedWindows[i + 1].Start)
                {
                    errors.Add($"Overlapping time windows in schedule '{scheduleId}': " +
                              $"{sortedWindows[i].Start}-{sortedWindows[i].End} and " +
                              $"{sortedWindows[i + 1].Start}-{sortedWindows[i + 1].End}");
                }
            }
        }

        private void ValidateOverrides(Overrides overrides, List<string> errors)
        {
            if (overrides.EnabledDates != null)
            {
                foreach (var date in overrides.EnabledDates)
                {
                    ValidateDate(date, "EnabledDate", errors);
                }
            }

            if (overrides.CustomScheduleDates != null)
            {
                var uniqueDates = new HashSet<DateTime>();

                foreach (var custom in overrides.CustomScheduleDates)
                {
                    ValidateCustomScheduleDate(custom, errors);

                    if (!uniqueDates.Add(custom.Date))
                    {
                        errors.Add($"Duplicate custom schedule date: {custom.Date:yyyy-MM-dd}");
                    }
                }
            }

            if (overrides.EnabledDates != null && overrides.CustomScheduleDates != null)
            {
                var enabledSet = new HashSet<DateTime>(overrides.EnabledDates);
                var customSet = new HashSet<DateTime>(overrides.CustomScheduleDates.Select(c => c.Date));

                var intersection = enabledSet.Intersect(customSet).ToList();
                if (intersection.Any())
                {
                    errors.Add($"Dates present in both EnabledDates and CustomScheduleDates: " +
                              $"{string.Join(", ", intersection.Select(d => d.ToString("yyyy-MM-dd")))}");
                }
            }
        }

        private void ValidateCustomScheduleDate(CustomScheduleDate custom, List<string> errors)
        {
            ValidateDate(custom.Date, "CustomScheduleDate", errors);

            if (string.IsNullOrEmpty(custom.ScheduleType) &&
                (custom.Windows == null || !custom.Windows.Any()))
            {
                errors.Add($"CustomScheduleDate for {custom.Date:yyyy-MM-dd} must have either ScheduleType or Windows");
            }

            if (!string.IsNullOrEmpty(custom.ScheduleType) && custom.ScheduleType.Length > 50)
            {
                errors.Add($"ScheduleType '{custom.ScheduleType}' is too long (max 50 chars)");
            }

            if (custom.Windows != null && custom.Windows.Any())
            {
                foreach (var window in custom.Windows)
                {
                    ValidateTimeWindow(window, $"custom_{custom.Date:yyyy-MM-dd}", errors);
                }

                CheckForOverlappingWindows(custom.Windows, $"custom_{custom.Date:yyyy-MM-dd}", errors);
            }

        }

        private void ValidateDate(DateTime date, string context, List<string> errors)
        {
            if (date == DateTime.MinValue || date == DateTime.MaxValue)
            {
                errors.Add($"Invalid date in {context}");
            }

            if (date.Year < 2000 || date.Year > 2100)
            {
                errors.Add($"Date {date:yyyy-MM-dd} in {context} is outside valid range (2000-2100)");
            }
        }

        // Метод для проверки строки времени без парсинга
        public bool IsValidTimeString(string time)
        {
            return !string.IsNullOrEmpty(time) && _timeRegex.IsMatch(time);
        }

        public bool IsValidDateString(string date)
        {
            return DateTime.TryParseExact(date, DateFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        private void PostProcessSchedule(AlertSchedule schedule)
        {
            schedule.DaySchedules ??= new List<DaySchedule>();
            schedule.DisabledDates ??= new List<DateTime>();
            schedule.Overrides ??= new Overrides();
            schedule.Overrides.EnabledDates ??= new List<DateTime>();
            schedule.Overrides.CustomScheduleDates ??= new List<CustomScheduleDate>();

            // Сортировка DisabledDates
            if (schedule.DisabledDates.Any())
            {
                schedule.DisabledDates = schedule.DisabledDates.OrderBy(d => d).ToList();
            }

            foreach (var daySchedule in schedule.DaySchedules)
            {
                if (string.IsNullOrEmpty(daySchedule.Id))
                {
                    daySchedule.Id = Guid.NewGuid().ToString();
                }
                daySchedule.Days ??= new List<DayOfWeek>();
                daySchedule.Windows ??= new List<TimeWindow>();

                if (daySchedule.Windows.Any())
                {
                    daySchedule.Windows = daySchedule.Windows.OrderBy(w => w.Start).ToList();
                }
            }

            if (schedule.Overrides.CustomScheduleDates.Any())
            {
                schedule.Overrides.CustomScheduleDates = schedule.Overrides.CustomScheduleDates
                    .OrderBy(c => c.Date)
                    .ToList();
            }
        }
    }


    public class TimeSpanYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(TimeSpan);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalar = parser.Consume<Scalar>();
            return TimeSpan.Parse(scalar.Value);
        }


        public void WriteYaml(IEmitter emitter, object value, Type type)
        {
            var timeSpan = (TimeSpan)value;
            emitter.Emit(new Scalar(timeSpan.ToString(@"hh\:mm")));
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }

    public class DayOfWeekYamlConverter : IYamlTypeConverter
    {
        public bool Accepts(Type type) => type == typeof(DayOfWeek);

        public object ReadYaml(IParser parser, Type type, ObjectDeserializer rootDeserializer)
        {
            var scalar = parser.Consume<Scalar>();
            return scalar.Value.ToLower() switch
            {
                "mon" => DayOfWeek.Monday,
                "tue" => DayOfWeek.Tuesday,
                "wed" => DayOfWeek.Wednesday,
                "thu" => DayOfWeek.Thursday,
                "fri" => DayOfWeek.Friday,
                "sat" => DayOfWeek.Saturday,
                "sun" => DayOfWeek.Sunday,
                _ => Enum.Parse<DayOfWeek>(scalar.Value, true)
            };
        }

        public void WriteYaml(IEmitter emitter, object value, Type type, ObjectSerializer serializer)
        {
            var day = (DayOfWeek)value;
            var shortName = day.ToString()[..3];
            emitter.Emit(new Scalar(shortName));
        }

    }
}
