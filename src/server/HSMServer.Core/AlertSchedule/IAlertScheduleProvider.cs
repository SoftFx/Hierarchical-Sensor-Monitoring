using System;
using System.Collections.Generic;
using HSMServer.Core.Model.Policies;

namespace HSMServer.Core.Schedule
{
    public interface IAlertScheduleProvider
    {
        List<AlertSchedule> GetAllSchedules();

        AlertSchedule GetSchedule(Guid id);

        void SaveSchedule(AlertSchedule schedule);

        void DeleteSchedule(Guid Id);

        bool IsWorkingTime(Guid id, DateTime time);
    }
}
