using System;
using System.Collections.Generic;
using System.Linq;

namespace HSMDatabase.AccessManager.TableBatches
{
    /// <summary>A run of merge hours <c>[FirstHour, EndHour)</c> whose rows of a sensor are in the databases of class <see cref="PeriodWeeks"/>.</summary>
    public readonly record struct TablePlacementSegment(long FirstHour, long EndHour, int PeriodWeeks);


    /// <summary>
    /// Where the batches of one table sensor are (#1850 part 4b, ADR-0021 point 12): the period class its hours are merged
    /// into now (<see cref="Target"/>), and the runs of earlier hours whose rows are still in another class, merged there
    /// before a change of the sensor's period and not moved yet. The class of an hour is <see cref="ClassOf"/>: the merge
    /// writes an hour's rows of the sensor into it and every read looks there, so the two never disagree. Kept durably by
    /// the batch store, per product (<see cref="ITableBatchStore.SetPlacement"/>).
    /// </summary>
    public sealed record TablePlacement
    {
        public TablePlacement(int target, IReadOnlyList<TablePlacementSegment> segments = null)
        {
            if (target < 0 || target > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(target), target, "a period class of 0 to 65,535 weeks");

            Target = target;
            Segments = segments ?? [];

            for (int i = 0; i < Segments.Count; i++)
            {
                var s = Segments[i];
                if (s.FirstHour >= s.EndHour || s.PeriodWeeks == target || s.PeriodWeeks < 0 || s.PeriodWeeks > ushort.MaxValue ||
                    (i > 0 && s.FirstHour < Segments[i - 1].EndHour))
                    throw new ArgumentException("Segments are non-empty, ordered, apart, and of a class other than the target.", nameof(segments));
            }
        }


        /// <summary>The class the sensor's hours are merged into from the last change on (its history setting in whole weeks).</summary>
        public int Target { get; }

        /// <summary>Earlier hours whose rows are in another class, in hour order, apart; hours outside them are in <see cref="Target"/>.</summary>
        public IReadOnlyList<TablePlacementSegment> Segments { get; }

        /// <summary>The class holding the sensor's rows of a merge hour.</summary>
        public int ClassOf(long hour)
        {
            foreach (var segment in Segments)
                if (hour >= segment.FirstHour && hour < segment.EndHour)
                    return segment.PeriodWeeks;

            return Target;
        }

        /// <summary>
        /// The placement after the sensor's period changes to <paramref name="target"/>, effective from the merge hour
        /// <paramref name="cutoffHour"/> (an hour no part of which can be on disk yet, so a merge that repeats an earlier hour
        /// gives every sensor the class the earlier run gave it). Every hour before it keeps the class it has: the hours in
        /// the old target before the cut-off become a segment of the old target, and a segment whose class is the new target
        /// goes (its rows are where the sensor's hours now belong). Hours from the cut-off on take the new target.
        /// </summary>
        public TablePlacement Retarget(int target, long cutoffHour)
        {
            if (target == Target)
                return this;

            if (Segments.Count > 0 && cutoffHour < Segments[^1].EndHour)
                throw new ArgumentOutOfRangeException(nameof(cutoffHour), cutoffHour, "the cut-off of a change is never before an earlier change");

            var result = new List<TablePlacementSegment>();
            long covered = long.MinValue;

            foreach (var segment in Segments)
            {
                // The hours between the earlier segments were in the old target.
                if (segment.FirstHour > covered)
                    result.Add(new TablePlacementSegment(covered, segment.FirstHour, Target));

                result.Add(segment);
                covered = segment.EndHour;
            }

            if (cutoffHour > covered)
                result.Add(new TablePlacementSegment(covered, cutoffHour, Target));

            return new TablePlacement(target, Merge(result.Where(s => s.PeriodWeeks != target)));
        }

        /// <summary>The placement without the hours <c>[firstHour, endHour)</c> in segments: those hours are in the target now (the move).</summary>
        public TablePlacement WithoutHours(long firstHour, long endHour)
        {
            var result = new List<TablePlacementSegment>();

            foreach (var s in Segments)
            {
                if (s.EndHour <= firstHour || s.FirstHour >= endHour)
                {
                    result.Add(s);
                    continue;
                }

                if (s.FirstHour < firstHour)
                    result.Add(s with { EndHour = firstHour });
                if (s.EndHour > endHour)
                    result.Add(s with { FirstHour = endHour });
            }

            return new TablePlacement(Target, result);
        }

        // Adjacent segments of one class become one.
        private static List<TablePlacementSegment> Merge(IEnumerable<TablePlacementSegment> segments)
        {
            var merged = new List<TablePlacementSegment>();

            foreach (var s in segments)
                if (merged.Count > 0 && merged[^1].EndHour == s.FirstHour && merged[^1].PeriodWeeks == s.PeriodWeeks)
                    merged[^1] = merged[^1] with { EndHour = s.EndHour };
                else
                    merged.Add(s);

            return merged;
        }

        public bool Equals(TablePlacement other) =>
            other is not null && Target == other.Target && Segments.SequenceEqual(other.Segments);

        public override int GetHashCode() => HashCode.Combine(Target, Segments.Count);
    }
}
