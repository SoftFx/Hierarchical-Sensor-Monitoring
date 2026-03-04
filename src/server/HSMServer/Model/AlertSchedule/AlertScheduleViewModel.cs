using HSMDatabase.AccessManager.DatabaseEntities;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;


namespace HSMServer.Model.AlertSchedule
{
    public class AlertScheduleViewModel
    {

        public Guid Id { get; set; } = Guid.Empty;

        public string Name { get; set; }

        public string TimeZone { get; set; }

        public List<SelectListItem> TimeZoneList { get; set; }

        public string Schedule { get; set; }

        public AlertScheduleViewModel()
        {
            TimeZoneList = InitTimeZoneList();
            Schedule = @"daySchedules:
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
        }

        public AlertScheduleViewModel(AlertScheduleEntity entity) 
        {
            Id = new Guid(entity.Id);
            Name = entity.Name;
            TimeZone = entity.TimeZone;
            TimeZoneList = InitTimeZoneList();
            Schedule = entity.Schedule;
        }

        private List<SelectListItem> InitTimeZoneList()
        {
            return TimeZoneInfo.GetSystemTimeZones()
            .Select(tz => new SelectListItem
            {
                Value = tz.Id,
                Text = $"{tz.DisplayName} ({tz.Id})"
            })
            .OrderBy(tz => tz.Text)
            .ToList();
        }

    }
}
