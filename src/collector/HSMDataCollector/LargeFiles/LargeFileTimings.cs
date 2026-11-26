using System;
using System.Diagnostics;
using System.Threading;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>What a wait of the uploader is for: a test seam treats them differently.</summary>
    internal enum WaitKind
    {
        /// <summary>Between two scans of the watched folders, or while nothing is to do.</summary>
        Scan,
        /// <summary>A server's <c>retryAfter</c>, a backoff after a lost connection.</summary>
        Retry,
        /// <summary>The speed limit, between two pieces.</summary>
        Throttle,
    }


    /// <summary>
    /// The clock and the waits of the large file uploader: a monotonic clock (Stopwatch; the wall clock may jump, and
    /// net472 has no <c>Environment.TickCount64</c>) for the settle rule, the retry times and the speed limit. Tests replace
    /// it (<see cref="Core.CollectorOptions.LargeFileTimings"/>) to decide time themselves.
    /// </summary>
    internal class LargeFileTimings
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();

        /// <summary>Monotonic time since the timings were made.</summary>
        internal virtual TimeSpan Now => _clock.Elapsed;

        /// <summary>
        /// Waits up to <paramref name="span"/>, ending early when <paramref name="wake"/> is set (a new file was handed over,
        /// a watch was added, the collector stops) or <paramref name="token"/> fires. True when the whole span passed.
        /// </summary>
        internal virtual bool Wait(TimeSpan span, WaitKind kind, WaitHandle wake, CancellationToken token)
        {
            if (span <= TimeSpan.Zero)
                return !token.IsCancellationRequested;

            var ms = span.TotalMilliseconds >= int.MaxValue ? int.MaxValue - 1 : (int)Math.Ceiling(span.TotalMilliseconds);
            var index = WaitHandle.WaitAny(new[] { wake, token.WaitHandle }, ms);

            return index == WaitHandle.WaitTimeout;
        }

        /// <summary>The interval between two scans of a watch with this settle time: a quarter of it, at most 10 s, at least 250 ms.</summary>
        internal virtual TimeSpan ScanInterval(TimeSpan settle)
        {
            var quarter = TimeSpan.FromTicks(settle.Ticks / 4);

            return quarter > TimeSpan.FromSeconds(10) ? TimeSpan.FromSeconds(10)
                 : quarter < TimeSpan.FromMilliseconds(250) ? TimeSpan.FromMilliseconds(250)
                 : quarter;
        }

        /// <summary>How long a handed-over file waits for another process's lock on its state file before it is put back.</summary>
        internal virtual TimeSpan StateLockWait => TimeSpan.FromSeconds(5);

        /// <summary>How long Stop waits for the uploader's thread.</summary>
        internal virtual TimeSpan StopWait => TimeSpan.FromSeconds(1);

        /// <summary>How long the thread lingers with nothing to do before it ends (it is made again when there is work).</summary>
        internal virtual TimeSpan IdleExit => TimeSpan.FromSeconds(30);

        /// <summary>How long a server without large files waits before it is asked again (a new session asks at once).</summary>
        internal virtual TimeSpan FeatureAbsentRetry => TimeSpan.FromMinutes(30);

        /// <summary>
        /// What the uploader reads of an open file, given what it is. Tests change it to make a file "change" while it is sent,
        /// which on Windows no other process can do to a file the collector holds.
        /// </summary>
        internal virtual FileFacts Observe(string path, FileFacts facts) => facts;

        /// <summary>For tests: the uploader has looked at its folders and queue after the last tick of the clock.</summary>
        internal virtual void Scanned() { }
    }
}
