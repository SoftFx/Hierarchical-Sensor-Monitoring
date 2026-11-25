using System;
using HSMServer.Core.Model;

namespace HSMServer.Core.TableSensors
{
    /// <summary>
    /// The retention period class of a table sensor (ADR-0021 point 12, #1850): its history setting in whole weeks,
    /// rounded up, at least 1; 0 for kept for ever. The batches of an hour go to the class the sensor has at the merge.
    /// <para>
    /// The same rule as the cleanup of ordinary sensor history (<c>TimeIntervalModel.TimeIsUp</c> and the retention floor
    /// in <c>TreeValuesCache</c>): what never expires there is kept for ever here. That includes a dynamic preset (a month,
    /// a year) inherited from a folder: the tree carries it as <see cref="TimeInterval.FromFolder"/> with 0 ticks, which
    /// says nothing about its length, and the ordinary cleanup never deletes such history either (an open question for the
    /// owner, not worked around here). A preset on the sensor or the product counts by its nominal length (a month 31 days:
    /// 5 weeks; three months 14; six months 27; a year 53).
    /// </para>
    /// </summary>
    public static class TablePeriodClass
    {
        public const int Forever = 0;

        private const long WeekTicks = 7 * TimeSpan.TicksPerDay;


        /// <summary>The class of a history setting; <paramref name="utcNow"/> bounds what can be shifted, as the ordinary cleanup's floor does.</summary>
        public static int Weeks(TimeIntervalModel policy, DateTime utcNow)
        {
            if (policy is null)
                return Forever;

            long ticks = policy.Interval switch
            {
                TimeInterval.Month or TimeInterval.ThreeMonths or TimeInterval.SixMonths or TimeInterval.Year => (long)policy.Interval,
                TimeInterval.Ticks or TimeInterval.FromFolder => policy.Ticks,
                _ => 0, // None, FromParent left unresolved: never expires
            };

            if (ticks <= 0 || ticks >= utcNow.Ticks)
                return Forever;

            long weeks = (ticks + WeekTicks - 1) / WeekTicks;

            // The record of a merged hour keeps the class in 16 bits; a longer period is for ever in practice.
            return weeks > ushort.MaxValue ? Forever : (int)Math.Max(1, weeks);
        }

        /// <summary>The class of a table sensor now: its history setting as resolved through its folder and product.</summary>
        public static int Of(BaseSensorModel sensor, DateTime utcNow) => Weeks(sensor.Settings.KeepHistory.Value, utcNow);
    }
}
