using System;
using System.Globalization;
using System.IO;

namespace HSMDatabase.AccessManager
{
    /// <summary>Where a weekly history shard stands in the life of a sealed week (#1804), as its marker files say.</summary>
    public enum SealedWeekStage
    {
        /// <summary>No chunk directory: the week is rows only.</summary>
        NotSealed,

        /// <summary>
        /// A chunk directory without <see cref="SealedWeekMarker.FileName"/>: a seal that is running or was interrupted.
        /// Not read; the week is rows only, and the next seal starts the directory again.
        /// </summary>
        IncompleteSeal,

        /// <summary>Sealed and complete; the overlay still holds every source row until the keep period ends.</summary>
        SealedKeepingSourceRows,

        /// <summary>Sealed and complete, and the source rows that equal their chunk rows are gone from the overlay.</summary>
        SourceRowsRemoved,

        /// <summary>
        /// An unseal began and has not finished. The week is sealed while <see cref="SealedWeekMarker.FileName"/> is there,
        /// and rows only once it is gone; the job finishes the unseal before it does anything else with the week.
        /// </summary>
        Unsealing,
    }


    /// <summary>The stage of a week as read from its marker files, without opening any database.</summary>
    /// <param name="Stage">The stage.</param>
    /// <param name="KeepSourceRowsUntilUtc">When the source rows of a sealed week may go; null when unknown (kept).</param>
    /// <param name="IsSealedMarkerPresent">The step-2 signal: chunks complete and read merged with the overlay.</param>
    /// <param name="UnsealedByOperator">An operator unsealed the week: the job leaves it as rows.</param>
    public readonly record struct SealedWeekStatus(SealedWeekStage Stage, DateTime? KeepSourceRowsUntilUtc, bool IsSealedMarkerPresent, bool UnsealedByOperator);


    /// <summary>
    /// The marker files of the sealing job (#1804). Each is a small human-readable text file written atomically (a
    /// temporary file flushed to disk, then renamed). <see cref="SealedWeekMarker.FileName"/> (step 2) stays the only
    /// signal readers act on: present means the chunks are complete and the week is read merged with its overlay. The
    /// others refine what the job still has to do and are never consulted by a read. LevelDB claims only CURRENT,
    /// LOCK, LOG*, MANIFEST-* and numbered files of a directory and ignores these.
    /// </summary>
    public static class SealedWeekStages
    {
        /// <summary>In the chunk directory while a seal writes it; informational (an incomplete seal is recognised without it).</summary>
        public const string SealingFileName = "hsm-sealing";

        /// <summary>In the chunk directory: the first line is the UTC time after which the source rows may be removed.</summary>
        public const string KeepSourceRowsUntilFileName = "hsm-keep-source-rows-until";

        /// <summary>In the chunk directory: the source rows that equal their chunk rows have been removed from the overlay.</summary>
        public const string SourceRowsRemovedFileName = "hsm-source-rows-removed";

        /// <summary>In the chunk directory: the sensors left as rows, with the reason and a fingerprint of their rows.</summary>
        public const string RowsLeftFileName = "hsm-rows-left";

        /// <summary>In the chunk directory: an unseal is in progress.</summary>
        public const string UnsealingFileName = "hsm-unsealing";

        /// <summary>In the row directory: an operator unsealed this week; the job does not seal it again until the file is deleted.</summary>
        public const string UnsealedFileName = "hsm-unsealed";


        public static SealedWeekStatus Read(string rowPath, string sealedPath)
        {
            var unsealedByOperator = File.Exists(Path.Combine(rowPath, UnsealedFileName));

            if (!Directory.Exists(sealedPath))
                return new(SealedWeekStage.NotSealed, null, false, unsealedByOperator);

            var sealedMarker = SealedWeekMarker.ExistsIn(sealedPath);

            if (File.Exists(Path.Combine(sealedPath, UnsealingFileName)))
                return new(SealedWeekStage.Unsealing, null, sealedMarker, unsealedByOperator);

            if (!sealedMarker)
                return new(SealedWeekStage.IncompleteSeal, null, false, unsealedByOperator);

            if (File.Exists(Path.Combine(sealedPath, SourceRowsRemovedFileName)))
                return new(SealedWeekStage.SourceRowsRemoved, null, true, unsealedByOperator);

            return new(SealedWeekStage.SealedKeepingSourceRows, ReadKeepUntil(sealedPath), true, unsealedByOperator);
        }

        /// <summary>The first line of the keep file as a UTC time; null when the file is missing or unreadable (the rows are then kept).</summary>
        public static DateTime? ReadKeepUntil(string sealedPath)
        {
            try
            {
                var path = Path.Combine(sealedPath, KeepSourceRowsUntilFileName);

                if (!File.Exists(path))
                    return null;

                using var reader = new StreamReader(path);
                var line = reader.ReadLine()?.Trim();

                return DateTime.TryParse(line, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var until)
                    ? DateTime.SpecifyKind(until, DateTimeKind.Utc)
                    : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }
}
