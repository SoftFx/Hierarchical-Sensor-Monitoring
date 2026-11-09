using System;
using System.ComponentModel;
using HSMServer.Model.ManagementApi.ChangeSets;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// The dry-run preview of a sensor deletion (#1637): everything the real
    /// write would destroy — the sensor, its attached policies (the inherited
    /// <c>affected</c> list), and its whole stored history. Deletion is
    /// irreversible (no undo), so the preview is the only "check before you
    /// pull the trigger" the surface offers.
    /// </summary>
    // The WritePreviewDto extension of the deletion surface: unlike a policy
    // write (whose blast radius is one list item), a deletion erases data that
    // cannot be re-sent — the history count, on-disk size and mute state tell
    // the operator WHAT is being lost, not just which entities vanish.
    // Carries [Description]s on every member: the record rides the MCP tool's
    // OUTPUT schema (#1553).
    [Description("The dry-run preview of a sensor deletion: everything the real write would destroy — nothing was applied.")]
    public sealed record SensorDeletePreviewDto : WritePreviewDto
    {
        /// <summary>
        /// Sensor lifecycle state at preview time. Value table: Available, Muted,
        /// Blocked — a Blocked sensor can be deleted too (same as the web UI).
        /// </summary>
        [Description("Sensor lifecycle state at preview time. Value table: Available, Muted, Blocked — a Blocked sensor can be deleted too (same as the web UI).")]
        public string State { get; init; }

        /// <summary>
        /// When the current mute lifts (UTC ISO 8601); null when the sensor is
        /// not muted or muted indefinitely — <c>state</c> distinguishes the two.
        /// </summary>
        [Description("When the current mute lifts (UTC ISO 8601); null when the sensor is not muted or muted indefinitely — `state` distinguishes the two.")]
        public DateTime? EndOfMuting { get; init; }

        /// <summary>
        /// Time of the newest stored value (UTC ISO 8601, the value's own
        /// measurement time); null when the sensor has no data yet.
        /// </summary>
        [Description("Time of the newest stored value (UTC ISO 8601, the value's own measurement time); null when the sensor has no data yet.")]
        public DateTime? LastValueTime { get; init; }

        /// <summary>Stored value count the deletion would erase (timeout markers
        /// included; computed by a full on-disk history scan — the same scan the
        /// web UI's node info runs, costly on sensors with millions of values).</summary>
        [Description("Stored value count the deletion would erase (timeout markers included; computed by a full on-disk history scan — costly on sensors with millions of values).")]
        public long HistoryValues { get; init; }

        /// <summary>On-disk size in bytes of the history the deletion would erase
        /// (part of the same full history scan as <c>historyValues</c>).</summary>
        [Description("On-disk size in bytes of the history the deletion would erase (part of the same full history scan as `historyValues`).")]
        public long HistorySizeBytes { get; init; }

        /// <summary>Reference to the sensor's root product.</summary>
        [Description("Reference to the sensor's root product.")]
        public NodeRefDto Product { get; init; }
    }
}
