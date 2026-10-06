using System;
using System.Collections.Generic;


namespace HSMCommon.Model
{
    /// <summary>
    /// Combines the standard deviations of several bars into the one of the bar that covers them
    /// all (#1509), from each part's (Count, Mean, StdDev) alone — no raw samples. Population
    /// (parallel-variance) form: with N = sum(n_i) and mu = sum(n_i * mu_i) / N,
    /// M2 = sum(n_i * (sigma_i^2 + (mu_i - mu)^2)) and sigma = sqrt(M2 / N). It is exact for the
    /// parts as given (a part's Mean and StdDev are the collector's rounded values).
    /// </summary>
    public static class BarStdDev
    {
        /// <returns>
        /// The combined StdDev, or <c>null</c> (unknown) when any counted part has an unknown
        /// StdDev or no part has samples. Parts with Count &lt;= 0 (e.g. timeout rows) carry no
        /// samples and are skipped. A single counted part keeps its own value unchanged.
        /// </returns>
        public static double? Combine(IReadOnlyList<(int Count, double Mean, double? StdDev)> parts)
        {
            long total = 0;
            double weightedSum = 0;
            int counted = 0;
            double? single = null;

            foreach (var (count, mean, stdDev) in parts)
            {
                if (count <= 0)
                    continue;

                if (stdDev is null)
                    return null;

                total += count;
                weightedSum += mean * count;
                single = stdDev;
                counted++;
            }

            if (counted == 0)
                return null;

            if (counted == 1)
                return single;

            var combinedMean = weightedSum / total;
            double m2 = 0;

            foreach (var (count, mean, stdDev) in parts)
            {
                if (count <= 0)
                    continue;

                var sigma = stdDev.Value;
                var shift = mean - combinedMean;

                m2 += count * (sigma * sigma + shift * shift);
            }

            return Math.Sqrt(m2 / total);
        }
    }
}
