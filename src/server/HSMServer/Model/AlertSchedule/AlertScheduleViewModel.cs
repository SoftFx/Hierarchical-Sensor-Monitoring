using HSMDatabase.AccessManager.DatabaseEntities;
using HSMServer.Core.Model;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;


namespace HSMServer.Model.AlertSchedule
{
    public class AlertScheduleViewModel
    {

        public Guid Id { get; set; } = Guid.Empty;

        [Required]
        public string Name { get; set; }

        [Required]
        public string Timezone { get; set; }


        private static readonly Lazy<List<SelectListItem>> _timezones = new(InitTimeZoneList);

        public List<SelectListItem> TimeZoneList => _timezones.Value;

        public string Schedule { get; set; }

        public List<BaseSensorModel> Sensors { get; set; } = new();

        public AlertScheduleViewModel()
        {
            Schedule ??= @"daySchedules:
    - days: [Mon, Tue, Wed, Thu, Fri]
      windows:
        - { start: ""09:00"", end: ""11:30"" }
        - { start: ""12:30"", end: ""15:00"" }

    - days: [Sat]
      windows:
        - { start: ""10:00"", end: ""14:00"" }

disabledDates: [""2026-02-11"", ""2026-02-23""]";
        }

        public AlertScheduleViewModel(AlertScheduleEntity entity) : this()
        {
            Id = new Guid(entity.Id);
            Name = entity.Name;
            Timezone = entity.Timezone;
            Schedule = entity.Schedule;
        }

        public AlertScheduleViewModel(Core.Model.Policies.AlertSchedule schedule) : this()
        {
            Id = schedule.Id;
            Name = schedule.Name;
            Timezone = schedule.Timezone;
            Schedule = schedule.Schedule;
        }

        private static List<SelectListItem> InitTimeZoneList()
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
