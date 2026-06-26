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
        private const string MemFreeKey = "MemFree:";
        private const string BuffersKey = "Buffers:";
        private const string CachedKey = "Cached:";
        private const string SReclaimableKey = "SReclaimable:";
        private const string ShmemKey = "Shmem:";

        /// <summary>
        /// Returns available memory in kB. Prefer <c>MemAvailable</c>; when older/custom kernels omit it,
        /// estimate using <c>MemFree + Buffers + Cached + SReclaimable - Shmem</c>.
        /// </summary>
        internal static long? ParseAvailableKb(string meminfoContent)
        {
            if (string.IsNullOrEmpty(meminfoContent))
                return null;

            long? memFree = null;
            long? buffers = null;
            long? cached = null;
            long? sReclaimable = null;
            long? shmem = null;

            foreach (var rawLine in meminfoContent.Split('\n'))
            {
                if (TryParseLine(rawLine, MemAvailableKey, out var memAvailable))
                    return memAvailable;

                if (TryParseLine(rawLine, MemFreeKey, out var value))
                {
                    memFree = value;
                    continue;
                }

                if (TryParseLine(rawLine, BuffersKey, out value))
                {
                    buffers = value;
                    continue;
                }

                if (TryParseLine(rawLine, CachedKey, out value))
                {
                    cached = value;
                    continue;
                }

                if (TryParseLine(rawLine, SReclaimableKey, out value))
                {
                    sReclaimable = value;
                    continue;
                }

                if (TryParseLine(rawLine, ShmemKey, out value))
                    shmem = value;
            }

            if (memFree.HasValue || buffers.HasValue || cached.HasValue || sReclaimable.HasValue || shmem.HasValue)
                return Math.Max(0L,
                    memFree.GetValueOrDefault() +
                    buffers.GetValueOrDefault() +
                    cached.GetValueOrDefault() +
                    sReclaimable.GetValueOrDefault() -
                    shmem.GetValueOrDefault());

            return null;
        }

        private static bool TryParseLine(string rawLine, string key, out long kb)
        {
            kb = 0L;

            if (!rawLine.StartsWith(key, StringComparison.Ordinal))
                return false;

            var rest = rawLine.Substring(key.Length);
            var parts = rest.Split(new[] { ' ', '\t', '\r' }, StringSplitOptions.RemoveEmptyEntries);

            return parts.Length >= 1 &&
                   long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out kb);
        }
    }
}
