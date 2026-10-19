using System;
using System.ComponentModel;

namespace HSMServer.Model.ManagementApi.SensorTree
{
    /// <summary>
    /// The optional body of the mute write: omitted (or a null
    /// <c>endOfMuting</c>) mutes indefinitely; an instant mutes until it,
    /// auto-unmuting at expiry.
    /// </summary>
    [Description("The optional body of the mute write: omitted (or a null `endOfMuting`) mutes indefinitely; an instant mutes until it, auto-unmuting at expiry.")]
    public sealed record SensorMuteRequestDto
    {
        /// <summary>
        /// Mute until this instant (UTC ISO 8601; must be in the future). Null =
        /// mute indefinitely (never auto-unmutes).
        /// </summary>
        [Description("Mute until this instant (UTC ISO 8601; must be in the future). Null = mute indefinitely (never auto-unmutes).")]
        public DateTime? EndOfMuting { get; init; }
    }
}
