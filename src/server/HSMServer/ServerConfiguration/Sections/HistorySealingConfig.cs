using System;
using System.Globalization;
using System.Text.Json.Serialization;
using HSMServer.Core.DataLayer;

namespace HSMServer.ServerConfiguration
{
    // The sealing job of sensor history (ADR-0020, #1804): closed weeks are encoded into chunks and their source rows
    // removed after a keep period. Read once at startup, like the Kestrel section: a change takes effect on the next
    // server start.
    public sealed class HistorySealingConfig
    {
        // Off until the rollout step (#1805) decides otherwise (ADR-0020, decision 10).
        public const bool DefaultEnabled = false;

        // A week is sealed this many days after it closed (its end is the next Monday 00:00 UTC). Late values are
        // harmless to a sealed week (they become overlay rows), so this only keeps the job off weeks that are still
        // busy with collectors catching up.
        public const int DefaultSealAfterDays = 7;

        // The source rows stay in the row database this many weeks after a verified seal: a defect the verification
        // missed can still be undone by an unseal that keeps them. ADR-0020 leaves the number to configuration.
        public const int DefaultKeepSourceRowsWeeks = 4;

        public const int DefaultFoldMinRows = 10_000;
        public const int DefaultFoldQuietHours = 24;

        private const int MaxSealAfterDays = 3650;
        private const int MaxKeepSourceRowsWeeks = 520;


        public bool Enabled { get; set; } = DefaultEnabled;

        public int SealAfterDays { get; set; } = DefaultSealAfterDays;

        // At least one week: the hand-off rule of #1803 (a source row is never deleted on the strength of a marker that
        // was just written) relies on the keep period being long, and on Linux a power loss can drop a rename that was
        // not yet flushed.
        public int KeepSourceRowsWeeks { get; set; } = DefaultKeepSourceRowsWeeks;

        // The daily UTC window the job works in, "HH:mm". Equal values (the default) mean all day; a window may wrap past
        // midnight ("22:00" to "06:00"). An operator's unseal ignores it.
        public string WorkingHoursStartUtc { get; set; } = "00:00";

        public string WorkingHoursEndUtc { get; set; } = "00:00";

        // A sealed week whose overlay holds this many rows to fold (late values, edits) is folded at the next pass; fewer
        // rows are folded once the week has not changed for FoldQuietHours.
        public int FoldMinRows { get; set; } = DefaultFoldMinRows;

        public int FoldQuietHours { get; set; } = DefaultFoldQuietHours;


        public void Validate()
        {
            const string section = nameof(IServerConfig.HistorySealing);

            if (SealAfterDays is < 1 or > MaxSealAfterDays)
                throw new InvalidOperationException($"{section}.{nameof(SealAfterDays)} = {SealAfterDays} is invalid: expected 1..{MaxSealAfterDays}.");

            if (KeepSourceRowsWeeks is < 1 or > MaxKeepSourceRowsWeeks)
                throw new InvalidOperationException($"{section}.{nameof(KeepSourceRowsWeeks)} = {KeepSourceRowsWeeks} is invalid: expected 1..{MaxKeepSourceRowsWeeks}.");

            if (FoldMinRows < 1)
                throw new InvalidOperationException($"{section}.{nameof(FoldMinRows)} = {FoldMinRows} is invalid: expected a positive number of rows.");

            if (FoldQuietHours < 0)
                throw new InvalidOperationException($"{section}.{nameof(FoldQuietHours)} = {FoldQuietHours} is invalid: expected 0 or more hours.");

            ParseTime(WorkingHoursStartUtc, nameof(WorkingHoursStartUtc));
            ParseTime(WorkingHoursEndUtc, nameof(WorkingHoursEndUtc));
        }

        [JsonIgnore]
        public HistorySealingSettings Settings => new()
        {
            Enabled = Enabled,
            SealAfter = TimeSpan.FromDays(SealAfterDays),
            KeepSourceRows = TimeSpan.FromDays(7 * KeepSourceRowsWeeks),
            WorkingHoursStartUtc = ParseTime(WorkingHoursStartUtc, nameof(WorkingHoursStartUtc)),
            WorkingHoursEndUtc = ParseTime(WorkingHoursEndUtc, nameof(WorkingHoursEndUtc)),
            FoldMinRows = FoldMinRows,
            FoldQuietPeriod = TimeSpan.FromHours(FoldQuietHours),
        };


        private static TimeSpan ParseTime(string value, string name)
        {
            if (TimeSpan.TryParseExact(value?.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var time) && time < TimeSpan.FromDays(1))
                return time;

            throw new InvalidOperationException($"{nameof(IServerConfig.HistorySealing)}.{name} = '{value}' is invalid: expected a UTC time of day as HH:mm.");
        }
    }
}
