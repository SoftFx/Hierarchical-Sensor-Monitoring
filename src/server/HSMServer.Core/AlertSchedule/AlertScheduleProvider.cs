using System;
using System.Collections.Generic;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;


namespace HSMServer.Core.Schedule
{
    public class AlertScheduleProvider : IAlertScheduleProvider
    {
        private readonly IDatabaseCore _database;
        private readonly AlertScheduleParser _parser = new();

        private readonly Dictionary<Guid, AlertSchedule> _schedules = new();

        private readonly object _lock = new object();

        public AlertScheduleProvider(IDatabaseCore database)
        {
            _database = database;
            LoadSchedulesFromDatabase();
        }

        public void DeleteSchedule(Guid id)
        {
            lock (_lock)
            {
                _schedules.Remove(id);
                _database.RemoveAlertSchedule(id);
            }
        }

        public List<AlertSchedule> GetAllSchedules()
        {
            lock (_lock)
            {
                return [.. _schedules.Values];
            }
        }

        public AlertSchedule GetSchedule(Guid id)
        {
            lock (_lock)
            {
                _schedules.TryGetValue(id, out var schedule);
                return schedule;
            }
        }

        public void SaveSchedule(AlertSchedule schedule)
        {
            lock (_lock)
            {
                _schedules[schedule.Id] = schedule;
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
                    catch
                    {
                        schedule = new AlertSchedule()
                        {
                            Id = new Guid(entity.Id),
                            Name = entity.Name,
                            Timezone = entity.Timezone,
                            Schedule = entity.Schedule,
                        };
                    }
                    _schedules[schedule.Id] = schedule;
                }
            }
        }
    }
}
