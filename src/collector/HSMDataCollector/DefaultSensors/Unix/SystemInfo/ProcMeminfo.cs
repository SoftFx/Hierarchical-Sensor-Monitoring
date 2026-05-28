using System;
using System.Globalization;


namespace HSMDataCollector.DefaultSensors.Unix.SystemInfo
{
    /// <summary>
    /// Pure parser for <c>/proc/meminfo</c>. Kept free of file I/O so it can be unit-tested with sample
    /// text on any OS. Lines look like <c>MemAvailable:   12345 kB</c> (value is always in kB).
    /// </summary>
    internal static class ProcMeminfo
    {
        private const string MemAvailableKey = "MemAvailable:";

        /// <summary>Returns the <c>MemAvailable</c> value in kB, or null if the field is absent/unparseable.</summary>
        internal static long? ParseAvailableKb(string meminfoContent)
        {
            if (string.IsNullOrEmpty(meminfoContent))
                return null;

            foreach (var rawLine in meminfoContent.Split('\n'))
            {
                if (!rawLine.StartsWith(MemAvailableKey, StringComparison.Ordinal))
                    continue;

                var rest = rawLine.Substring(MemAvailableKey.Length);
                var parts = rest.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 1 &&
                    long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb))
                    return kb;

                return null;
            }

            return null;
        }
    }
}
