using System;
using System.Collections.Generic;
using System.Linq;
using Occurify;
using NodaTime;
using HSMServer.Core.Model.Policies;
using Occurify.TimeZones;

namespace HSMServer.Core.Schedule
{
    public interface IAlertScheduleProvider
    {
        List<AlertSchedule> GetAllSchedules();

        void SaveSchedule(AlertSchedule schedule);

        void DeleteSchedule(Guid Id);

    }
}
