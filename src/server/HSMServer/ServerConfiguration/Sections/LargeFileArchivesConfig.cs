using System;
using System.Text.Json.Serialization;

namespace HSMServer.ServerConfiguration
{
    // The archive side of large file values (ADR-0022 points 13-15, #1885): how archives are packed, how long a replaced
    // dump password stays readable and how the download log is kept. Read once at startup, like the other sections: a
    // change takes effect on the next server start.
    public sealed class LargeFileArchivesConfig
    {
        // Deflate 1 packs a dump-like file 1.7 times faster than 6 for an archive about 4% larger (the step 3 trials).
        public const int DefaultDeflateLevel = 1;

        // A replaced password stays readable to a Manager or an admin this long after its last archive was re-encrypted.
        public const int DefaultPasswordRetentionDays = 30;

        // The requests of one caller for one archive make one download record while they keep coming within this window.
        public const int DefaultDownloadLogWindowMinutes = 10;

        public const int DefaultDownloadLogRetentionDays = 365;

        // The callers the coalescing window remembers at most; the oldest are forgotten first.
        public const int DefaultDownloadLogMaxCallers = 10_000;

        private const int MaxDeflateLevel = 9;
        private const int MaxPasswordRetentionDays = 3650;
        private const int MaxDownloadLogWindowMinutes = 24 * 60;
        private const int MaxDownloadLogRetentionDays = 3650;
        private const int MaxDownloadLogMaxCallers = 10_000_000;


        // 0 stores the content without compression (the encryption still applies); 9 is the smallest and slowest.
        public int DeflateLevel { get; set; } = DefaultDeflateLevel;

        public int PasswordRetentionDays { get; set; } = DefaultPasswordRetentionDays;

        public int DownloadLogWindowMinutes { get; set; } = DefaultDownloadLogWindowMinutes;

        public int DownloadLogRetentionDays { get; set; } = DefaultDownloadLogRetentionDays;

        public int DownloadLogMaxCallers { get; set; } = DefaultDownloadLogMaxCallers;


        [JsonIgnore]
        public TimeSpan PasswordRetention => TimeSpan.FromDays(PasswordRetentionDays);

        [JsonIgnore]
        public TimeSpan DownloadLogWindow => TimeSpan.FromMinutes(DownloadLogWindowMinutes);

        [JsonIgnore]
        public TimeSpan DownloadLogRetention => TimeSpan.FromDays(DownloadLogRetentionDays);


        public void Validate()
        {
            const string section = nameof(IServerConfig.LargeFileArchives);

            if (DeflateLevel is < 0 or > MaxDeflateLevel)
                throw new InvalidOperationException($"{section}.{nameof(DeflateLevel)} = {DeflateLevel} is invalid: expected 0..{MaxDeflateLevel}.");

            if (PasswordRetentionDays is < 0 or > MaxPasswordRetentionDays)
                throw new InvalidOperationException($"{section}.{nameof(PasswordRetentionDays)} = {PasswordRetentionDays} is invalid: expected 0..{MaxPasswordRetentionDays}.");

            if (DownloadLogWindowMinutes is < 0 or > MaxDownloadLogWindowMinutes)
                throw new InvalidOperationException($"{section}.{nameof(DownloadLogWindowMinutes)} = {DownloadLogWindowMinutes} is invalid: expected 0..{MaxDownloadLogWindowMinutes}.");

            if (DownloadLogRetentionDays is < 1 or > MaxDownloadLogRetentionDays)
                throw new InvalidOperationException($"{section}.{nameof(DownloadLogRetentionDays)} = {DownloadLogRetentionDays} is invalid: expected 1..{MaxDownloadLogRetentionDays}.");

            if (DownloadLogMaxCallers is < 1 or > MaxDownloadLogMaxCallers)
                throw new InvalidOperationException($"{section}.{nameof(DownloadLogMaxCallers)} = {DownloadLogMaxCallers} is invalid: expected 1..{MaxDownloadLogMaxCallers}.");
        }
    }
}
