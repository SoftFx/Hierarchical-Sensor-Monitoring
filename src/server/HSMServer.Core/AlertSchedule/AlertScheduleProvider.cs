using System;
using System.Collections.Generic;
using System.Linq;
using NLog;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;



namespace HSMServer.Core.Schedule
{
    public class AlertScheduleProvider : IAlertScheduleProvider
    {
        private readonly IDatabaseCore _database;
        private readonly AlertScheduleParser _parser = new();
        private readonly Logger _logger = LogManager.GetCurrentClassLogger();

        private readonly Dictionary<Guid, CacheEntry> _cache = new();

        private readonly object _lock = new object();

        private class CacheEntry
        {
            public AlertSchedule Schedule { get; set; }
            public DateTime? CachedTime { get; set; }
            public bool? WorkingTimeResult { get; set; }

            public bool IsCacheValid(DateTime time)
            {
                if (!CachedTime.HasValue || !WorkingTimeResult.HasValue)
                    return false;

                return RoundToMinute(CachedTime.Value) == RoundToMinute(time);
            }

            private DateTime RoundToMinute(DateTime time)
            {
                return new DateTime(time.Year, time.Month, time.Day,
                    time.Hour, time.Minute, 0, time.Kind);
            }
        }


        public AlertScheduleProvider(IDatabaseCore database)
        {
            _database = database;
            LoadSchedulesFromDatabase();
        }

        public bool IsWorkingTime(Guid id, DateTime time)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(id, out var cacheEntry))
                {
                    if (cacheEntry.IsCacheValid(time))
                    {
                        return cacheEntry.WorkingTimeResult.Value;
                    }

                    var result = cacheEntry.Schedule.IsWorkingTime(time);

                    cacheEntry.CachedTime = time;
                    cacheEntry.WorkingTimeResult = result;

                    return result;
                }
                else
                {
                    _logger.Error($"Alert Schedule with id = {id} was not found.");
                    return true;
                }
            }
        }

        public void DeleteSchedule(Guid id)
        {
            lock (_lock)
            {
                _cache.Remove(id);
                _database.RemoveAlertSchedule(id);
            }
        }

        public List<AlertSchedule> GetAllSchedules()
        {
            lock (_lock)
            {
                return [.. _cache.Values.Select(x  => x.Schedule).ToList()];
            }
        }

        public AlertSchedule GetSchedule(Guid id)
        {
            lock (_lock)
            {
                _cache.TryGetValue(id, out var entry);
                return entry?.Schedule;
            }
        }

        public void SaveSchedule(AlertSchedule schedule)
        {
            lock (_lock)
            {
                if (_cache.TryGetValue(schedule.Id, out var cacheEntry))
                {
                    cacheEntry.Schedule = schedule;
                    cacheEntry.CachedTime = null;
                    cacheEntry.WorkingTimeResult = null;
                }
                else
                {
                    _cache[schedule.Id] = new CacheEntry
                    {
                        Schedule = schedule,
                    };
                }

                _database.AddAlertSchedule(schedule.ToEntity());
            }
        }

        private void LoadSchedulesFromDatabase()
        {
            var scheduleEntities = _database.GetAllAlertSchedules();
            lock (_lock)
            {
                foreach (var entity in scheduleEntities)
                {
                    AlertSchedule schedule = null;
                    try
                    {
                        schedule = _parser.Parse(entity);
                    }
                    catch (Exception ex)
                    {
                        _logger.Error(ex, $"Failed to parse alert schedule. Id = {entity.Id}, Name = {entity.Name}. Using raw entity as fallback.");
                        schedule = new AlertSchedule()
                        {
                            Id = new Guid(entity.Id),
                            Name = entity.Name,
                            Timezone = entity.Timezone,
                            Schedule = entity.Schedule,
                        };
                    }

                    _cache[schedule.Id] = new CacheEntry
                    {
                       Schedule = schedule,
                    };
                }
            }
        }
    }
}
