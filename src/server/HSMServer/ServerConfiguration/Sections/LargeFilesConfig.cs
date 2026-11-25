using System;
using HSMDatabase.AccessManager.LargeFiles;
using HSMServer.Core.LargeFiles;

namespace HSMServer.ServerConfiguration
{
    // Large file values (ADR-0022, #1883): where the large file store keeps its files and its limits. Read once at startup
    // and validated with an error that names the key. Nothing here starts any work: the folder is created by the first
    // upload. There is no piece-size setting: the piece size is the caller's (the file connection, #1884).
    public sealed class LargeFilesConfig
    {
        // Relative to the working directory, like the Databases folder (so "LargeFiles" lies next to it), or absolute (it
        // may be another volume). Not inside Databases: the store is not a database and is not part of the backups.
        public string Folder { get; set; } = HSMDatabase.Settings.DatabaseSettings.DefaultLargeFilesFolder;

        // The largest file a begin accepts.
        public long MaxFileSizeBytes { get; set; } = LargeFileStoreLimits.DefaultMaxFileSizeBytes;

        // The uploads in progress at once (live leases); a begin beyond them is answered "too many uploads".
        public int MaxUploadsInProgress { get; set; } = LargeFileStoreLimits.DefaultMaxUploadsInProgress;

        // A begin is refused unless the free space of the store's volume, less this, less the reservations of the uploads
        // not packed yet, holds the announced size; 0 keeps only the reservations.
        public long MinFreeDiskMb { get; set; } = LargeFileStoreLimits.DefaultMinFreeDiskMb;

        // An uploading part is removed this many days after its last piece (a verified file never for age).
        public int AbandonedUploadDays { get; set; } = LargeFileStoreLimits.DefaultAbandonedUploadDays;

        // A lease without an append for this long is no longer live (two session silence limits by default).
        public int LeaseTimeoutSeconds { get; set; } = LargeFileStoreLimits.DefaultLeaseTimeoutSeconds;


        public const long MaxFileSizeLimit = 1L << 50;


        public void Validate() => _ = ToOptions();

        public LargeFileOptions ToOptions()
        {
            if (string.IsNullOrWhiteSpace(Folder))
                throw new InvalidOperationException($"{nameof(IServerConfig.LargeFiles)}.{nameof(Folder)} is empty: expected a folder, relative to the working directory or absolute.");

            Check(MaxFileSizeBytes, 1, MaxFileSizeLimit, nameof(MaxFileSizeBytes));
            Check(MaxUploadsInProgress, 1, 1024, nameof(MaxUploadsInProgress));
            Check(MinFreeDiskMb, 0, long.MaxValue / (1024 * 1024), nameof(MinFreeDiskMb));
            Check(AbandonedUploadDays, 1, 3650, nameof(AbandonedUploadDays));
            Check(LeaseTimeoutSeconds, 10, 86_400, nameof(LeaseTimeoutSeconds));

            return new LargeFileOptions
            {
                MaxFileSizeBytes = MaxFileSizeBytes,
                MaxUploadsInProgress = MaxUploadsInProgress,
                MinFreeDiskMb = MinFreeDiskMb,
                AbandonedUploadDays = AbandonedUploadDays,
                LeaseTimeoutSeconds = LeaseTimeoutSeconds,
            };
        }

        private static void Check(long value, long min, long max, string name)
        {
            if (value < min || value > max)
                throw new InvalidOperationException($"{nameof(IServerConfig.LargeFiles)}.{name} = {value} is invalid: expected {min}..{max}.");
        }
    }
}
