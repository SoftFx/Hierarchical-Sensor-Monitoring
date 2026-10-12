namespace HSMServer.Model.ManagementApi.AlertSchedules
{
    /// <summary>
    /// The write request of the schedule CRUD: the three admin-editable
    /// fields of a schedule. The id never travels in the body — POST
    /// generates one server-side, PUT takes it from the route (the templates
    /// id-handling rule: the provider's Save is an upsert by id, so honoring
    /// a client-chosen id would let a caller overwrite a schedule it never
    /// meant to touch).
    /// </summary>
    public sealed record AlertScheduleUpsertDto
    {
        /// <summary>Schedule name; unique per server, 1..200 chars.</summary>
        public string Name { get; init; }

        /// <summary>System timezone id (e.g. "UTC", "Europe/Berlin", "Eastern Standard Time" — IANA and Windows both resolve); validated server-side.</summary>
        public string Timezone { get; init; }

        /// <summary>The working-time schedule as YAML text — the format the web UI's editor saves.</summary>
        public string Schedule { get; init; }
    }
}
