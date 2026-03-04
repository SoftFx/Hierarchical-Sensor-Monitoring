

using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.DataLayer;
using HSMServer.Core.Model.Policies;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMServer.Core.Schedule
{
    public class AlertScheduleProvider : IAlertScheduleProvider
    {
        private readonly IDatabaseCore _database;
        private readonly AlertScheduleParser _parser = new();

        private readonly object _lock = new object();

        public AlertScheduleProvider(IDatabaseCore database)
        {
            _database = database;
        }

        public void DeleteSchedule(Guid Id)
        {
            lock (_lock)
            {
                _database.RemoveAlertSchedule(Id);
            }
        }

        public List<AlertSchedule> GetAllSchedules()
        {
            lock (_lock)
            {
                return [.. _database.GetAllAlertSchedules().Select(_parser.Parse)];
            }
        }

        public void SaveSchedule(string yaml)
        {

        }

        public void SaveSchedule(AlertSchedule schedule)
        {
            throw new NotImplementedException();
        }
    }
}
