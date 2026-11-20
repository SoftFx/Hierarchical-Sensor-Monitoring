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
        /// The remains of an interrupted seal: a chunk directory with <see cref="SealedWeekStages.SealingFileName"/> and none
        /// of the markers a seal writes later. Not read; the week is rows only, and the next seal deletes and restarts it.
        /// </summary>
        IncompleteSeal,

        /// <summary>Sealed and complete; the overlay still holds every source row until the keep period ends.</summary>
        SealedKeepingSourceRows,

        /// <summary>Sealed and complete, and the source rows that equal their chunk rows are gone from the overlay.</summary>
        SourceRowsRemoved,

        /// <summary>
        /// An unseal began and has not finished: <see cref="SealedWeekMarker.FileName"/> is still there, the week is sealed,
        /// and the job finishes the unseal before it does anything else with the week. Deleting
        /// <see cref="SealedWeekStages.UnsealingFileName"/> cancels it (the week returns to its previous stage).
        /// </summary>
        Unsealing,

        /// <summary>
        /// A finished unseal whose chunk directory is still there: every chunk row is in the overlay, the week is rows
        /// (<see cref="SealedWeekStages.UnsealedFileName"/> in the row directory, no <see cref="SealedWeekMarker.FileName"/>).
        /// The directory is only clean-up.
        /// </summary>
        UnsealedLeftover,

        /// <summary>
        /// A chunk directory without <see cref="SealedWeekMarker.FileName"/> that is neither an interrupted seal nor a
        /// finished unseal: the marker was lost (an operator, a partial restore, file-system damage) while the chunks may be
        /// the only copy of the week. The week is served from its rows alone; the job never deletes, seals or touches the
        /// directory and reports it. Recovery: put <c>hsm-sealed</c> back (any text) and restart, or delete the directory if
        /// the rows are known to be complete.
        /// </summary>
        NeedsAttention,
    }


    /// <summary>The stage of a week as read from its marker files, without opening any database.</summary>
    /// <param name="Stage">The stage.</param>
    /// <param name="KeepSourceRowsUntilUtc">When the source rows of a sealed week may go; null when unknown (kept).</param>
    /// <param name="IsSealedMarkerPresent">The step-2 signal: chunks complete and read merged with the overlay.</param>
    /// <param name="UnsealedByOperator">An operator unsealed the week: the job leaves it as rows.</param>
    /// <param name="Attention">Why an operator has to look at the week; null when nothing is wrong.</param>
    public readonly record struct SealedWeekStatus(SealedWeekStage Stage, DateTime? KeepSourceRowsUntilUtc, bool IsSealedMarkerPresent,
        bool UnsealedByOperator, string Attention = null)
    {
        /// <summary>
        /// The one rule that lets the job delete a week's chunk directory (#1804, review S1/T1): only (a) the remains of an
        /// interrupted seal, or (b) the leftover of a finished unseal. Any other chunk directory may hold the only copy of
        /// rows and is never deleted.
        /// </summary>
        public bool MayDeleteChunkDirectory => Stage is SealedWeekStage.IncompleteSeal or SealedWeekStage.UnsealedLeftover;
    }


    /// <summary>
    /// The marker files of the sealing job (#1804). Each is a small human-readable text file written atomically (a
    /// temporary file flushed to disk, then renamed). <see cref="SealedWeekMarker.FileName"/> (step 2) stays the only
    /// signal readers act on: present means the chunks are complete and the week is read merged with its overlay. The
    /// others refine what the job still has to do and are never consulted by a read. LevelDB claims only CURRENT,
    /// LOCK, LOG*, MANIFEST-* and numbered files of a directory and ignores these.
    /// </summary>
    public static class SealedWeekStages
    {
        /// <summary>In the chunk directory from before its database exists until the seal is done.</summary>
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

        /// <summary>The line of the <c>hsm-sealed</c> text that repeats the keep time (read when the keep file is missing).</summary>
        public const string KeepLinePrefix = "Source rows kept until: ";


        public static SealedWeekStatus Read(string rowPath, string sealedPath)
        {
            var unsealedByOperator = File.Exists(Path.Combine(rowPath, UnsealedFileName));

            if (!Directory.Exists(sealedPath))
                return new(SealedWeekStage.NotSealed, null, false, unsealedByOperator);

            bool Has(string name) => File.Exists(Path.Combine(sealedPath, name));

            var sealedMarker = SealedWeekMarker.ExistsIn(sealedPath);
            var unsealing = Has(UnsealingFileName);

            if (!sealedMarker)
            {
                // A seal writes hsm-sealing before its database, and the keep file only after hsm-sealed; an unseal writes
                // hsm-unsealing first and hsm-unsealed (row directory) only after every chunk row is in the overlay.
                if (Has(SealingFileName) && !Has(KeepSourceRowsUntilFileName) && !Has(SourceRowsRemovedFileName) && !unsealing)
                    return new(SealedWeekStage.IncompleteSeal, null, false, unsealedByOperator);

                if (unsealing && unsealedByOperator)
                    return new(SealedWeekStage.UnsealedLeftover, null, false, true);

                return new(SealedWeekStage.NeedsAttention, null, false, unsealedByOperator,
                    $"{Path.GetFileName(sealedPath)} has no {SealedWeekMarker.FileName} but is neither an interrupted seal nor a finished unseal " +
                    "(its marker was lost?): the week is served from its rows alone and the directory is left untouched. Put hsm-sealed back " +
                    "and restart if the chunks belong to the week, or delete the directory if its rows are complete.");
            }

            if (unsealing)
                return new(SealedWeekStage.Unsealing, null, true, unsealedByOperator);

            if (Has(SourceRowsRemovedFileName))
                return new(SealedWeekStage.SourceRowsRemoved, null, true, unsealedByOperator);

            var (until, problem) = ReadKeepUntil(sealedPath);

            return new(SealedWeekStage.SealedKeepingSourceRows, until, true, unsealedByOperator, problem);
        }

        /// <summary>
        /// The keep time: the first line of the keep file, or the keep line of <c>hsm-sealed</c> when the keep file is not
        /// written yet. A keep file that cannot be read, or neither source, leaves the time unknown (the rows are kept) and
        /// says why.
        /// </summary>
        public static (DateTime? until, string problem) ReadKeepUntil(string sealedPath)
        {
            var path = Path.Combine(sealedPath, KeepSourceRowsUntilFileName);

            try
            {
                if (File.Exists(path))
                {
                    var first = File.ReadLines(path).FirstOrDefaultLine();

                    return TryParse(first, out var until)
                        ? (until, null)
                        : (null, $"{path}: the first line '{first}' is not a UTC time (expected e.g. 2026-12-01T00:00:00.0000000Z); the source rows are kept until it is fixed");
                }

                foreach (var line in File.ReadLines(Path.Combine(sealedPath, SealedWeekMarker.FileName)))
                    if (line.StartsWith(KeepLinePrefix, StringComparison.Ordinal) && TryParse(line[KeepLinePrefix.Length..], out var fromMarker))
                        return (fromMarker, null);

                return (null, $"{path} is missing and {SealedWeekMarker.FileName} names no keep time; the source rows are kept until the file " +
                              "is written with a UTC time on its first line (e.g. 2026-12-01T00:00:00.0000000Z)");
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return (null, $"{path} cannot be read ({e.Message}); the source rows are kept");
            }
        }

        private static bool TryParse(string text, out DateTime until)
        {
            var ok = DateTime.TryParse(text?.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out until);

            until = DateTime.SpecifyKind(until, DateTimeKind.Utc);
            return ok;
        }

        private static string FirstOrDefaultLine(this System.Collections.Generic.IEnumerable<string> lines)
        {
            foreach (var line in lines)
                return line;

            return null;
        }
    }
}
