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

            public Dictionary<(DateTime StartTime, DateTime EndTime), bool> IntervalCache { get; set; } = new();


            public bool IsCacheValid(DateTime time)
            {
                if (!CachedTime.HasValue || !WorkingTimeResult.HasValue)
                    return false;

                return RoundToMinute(CachedTime.Value) == RoundToMinute(time);
            }

            public void AddIntervalToCache(DateTime startTime, DateTime endTime, bool result)
            {
                var key = (startTime, endTime);
                IntervalCache[key] = result;
            }

            public bool? GetCachedIntervalResult(DateTime startTime, DateTime endTime)
            {
                var key = (startTime, endTime);
                if (IntervalCache.TryGetValue(key, out var result))
                    return result;
                return null;
            }

            public void InvalidateCache()
            {
                CachedTime = null;
                WorkingTimeResult = null;
                IntervalCache.Clear();
            }

            private static DateTime RoundToMinute(DateTime time)
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

        public bool IsWorkingTime(Guid id, DateTime startTime, DateTime endTime)
        {
            if (startTime >= endTime)
                throw new ArgumentException("Start time must be less than end time");

            lock (_lock)
            {
                if (_cache.TryGetValue(id, out var cacheEntry))
                {
                    var cachedResult = cacheEntry.GetCachedIntervalResult(startTime, endTime);
                    if (cachedResult.HasValue)
                    {
                        return cachedResult.Value;
                    }

                    var result = cacheEntry.Schedule.IsWorkingTime(startTime, endTime);

                    cacheEntry.AddIntervalToCache(startTime, endTime, result);

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
                    cacheEntry.InvalidateCache();
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
                        _logger.Error(ex, $"Failed to parse AlertSchedule {new Guid(entity.Id)} ('{entity.Name}'). A stub schedule will be used.");
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
