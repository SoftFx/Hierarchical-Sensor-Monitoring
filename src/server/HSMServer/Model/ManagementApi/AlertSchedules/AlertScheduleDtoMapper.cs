using System;
using System.Collections.Generic;

namespace HSMServer.Model.ManagementApi.AlertSchedules
{
    // Credential-free mapping of the durable fields for /api/v1/alertSchedules; the
    // sensor filtering needs live services and stays in the controller.
    internal static class AlertScheduleDtoMapper
    {
        // Fully qualified: the sibling namespace HSMServer.Model.AlertSchedule shadows
        // the unqualified type name inside HSMServer.Model.*.
        internal static AlertScheduleDto ToDto(HSMServer.Core.Model.Policies.AlertSchedule schedule, List<string> visibleSensorPaths) => new()
        {
            Id = schedule.Id,
            Name = schedule.Name,
            Timezone = schedule.Timezone,
            Schedule = schedule.Schedule,
            Sensors = visibleSensorPaths ?? [],
        };
    }
}
