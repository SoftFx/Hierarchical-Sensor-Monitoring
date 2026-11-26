using System;

namespace HSMDataCollector.Options
{
    /// <summary>
    /// How large files of a watched folder, or one file handed over, are taken and uploaded (ADR-0022). Read once, when the
    /// watch is made or the file handed over; changing the object later changes nothing.
    /// </summary>
    public sealed class LargeFileOptions
    {
        /// <summary>The settle time by default: 60 seconds.</summary>
        public static readonly TimeSpan DefaultSettleTime = TimeSpan.FromSeconds(60);

        /// <summary>The speed limit by default: 10,000,000 bytes sent per second (10 MB/s).</summary>
        public static readonly long DefaultMaxBytesPerSecond = 10_000_000;


        /// <summary>
        /// How long a file's size and modification time must stay unchanged, as the collector observes them, before the
        /// file is taken. 60 s by default; at least 1 s. Watched folders only: a file handed over is complete by the caller's
        /// word.
        /// </summary>
        public TimeSpan SettleTime { get; set; } = DefaultSettleTime;

        /// <summary>
        /// True (the default): the source file is deleted after the server answered that it stored and checked the whole
        /// file, and only the file that was sent (on Windows known by its file id; the file is held open for the upload and
        /// others cannot write or delete it). False: the file is left, recorded as delivered in the collector's data folder
        /// (<see cref="Core.CollectorOptions.DataFolder"/>) and not offered again.
        /// </summary>
        public bool DeleteAfterUpload { get; set; } = true;

        /// <summary>
        /// The most bytes per second the upload sends (compressed pieces, as they go on the wire), kept by waiting between
        /// pieces: a piece, once begun, is sent at full speed. 10,000,000 (10 MB/s) by default; 0 means no limit.
        /// </summary>
        public long MaxBytesPerSecond { get; set; } = 10_000_000;

        /// <summary>
        /// True (the default): the File sensor is registered with <c>DefaultAlertsOptions.LargeFiles</c>, which asks the
        /// server for its default alert "new data has been received": one notification per stored file. False: the flag is
        /// not sent.
        /// </summary>
        public bool NewFileAlert { get; set; } = true;

        /// <summary>
        /// True (the default): the file is secure: the server keeps it as a zip encrypted under the product's dump password
        /// and gives it out only in that form (a dump holds the memory of a process). False: the same zip without a password,
        /// which everyone who may see the product can open. Said to the server when the upload begins and never changed
        /// afterwards.
        /// </summary>
        public bool Secure { get; set; } = true;

        /// <summary>
        /// The registration of the File sensor: description, location, history period, TTL, alerts, default alert flags.
        /// Null takes the defaults of <see cref="FileSensorOptions"/>. <c>MaxFileSizeBytes</c>, <c>DefaultFileName</c>,
        /// <c>Extension</c>, <c>IsPrioritySensor</c>, <c>AggregateData</c> and <c>PreviousPaths</c> do not apply to large
        /// files and are ignored (said once when set): the limit of a large file is the server's.
        /// </summary>
        public FileSensorOptions SensorOptions { get; set; }


        internal LargeFileOptions Snapshot() => (LargeFileOptions)MemberwiseClone();

        internal void Validate()
        {
            if (SettleTime < TimeSpan.FromSeconds(1))
                throw new ArgumentOutOfRangeException(nameof(SettleTime), "The settle time of large files must be at least one second.");

            if (MaxBytesPerSecond < 0)
                throw new ArgumentOutOfRangeException(nameof(MaxBytesPerSecond), "The speed limit of large files cannot be negative (0 means no limit).");
        }
    }
}
