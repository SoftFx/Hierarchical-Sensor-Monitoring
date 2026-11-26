using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HSMDataCollector.Options;
using HSMSensorDataObjects.SensorValueRequests;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>An ordinary file value above the threshold, sent on the file connection (ADR-0022 point 21).</summary>
    internal sealed class Routed
    {
        internal Routed(FileSensorValue value, TimeSpan queued)
        {
            Value = value;
            SensorPath = value.Path;
            Name = NameOf(value);
            Time = value.Time.Kind == DateTimeKind.Utc ? value.Time : value.Time.ToUniversalTime();
            Status = (int)value.Status;
            Comment = value.Comment;
            Queued = queued;
        }

        internal FileSensorValue Value { get; }

        internal string SensorPath { get; }

        /// <summary>The content: the value's own bytes, not copied (one copy in memory).</summary>
        internal List<byte> Content => Value.Value ?? Empty;

        private static readonly List<byte> Empty = new List<byte>();

        internal long Size => Content.Count;

        /// <summary>The upload began on the server (a begin was answered): it is not given back to the ordinary path.</summary>
        internal bool Began { get; set; }

        internal string Name { get; }

        /// <summary>The value's time: the <c>modified</c> of its begin (amendment, decision 5).</summary>
        internal DateTime Time { get; }

        internal int Status { get; }

        internal string Comment { get; }

        /// <summary>A new identity per send: two sends of one name and size are never taken for one file.</summary>
        internal string Source { get; } = "send:" + Guid.NewGuid().ToString("N");

        internal string Key => Source;

        internal TimeSpan Queued { get; }

        internal long MaxBytesPerSecond => LargeFileOptions.DefaultMaxBytesPerSecond;

        private static string NameOf(FileSensorValue value)
        {
            var name = string.IsNullOrEmpty(value.Name) ? "file" : value.Name;

            return string.IsNullOrEmpty(value.Extension) ? name : $"{name}.{value.Extension.TrimStart('.')}";
        }

        public override string ToString() => $"the file value {Name} of {SensorPath} ({Size} bytes)";
    }


    internal sealed partial class LargeFileService
    {
        private readonly Action<int> _countDroppedFileValues;
        private readonly Action<FileSensorValue> _requeueFile;

        // Under _gate: routed sends in the order they came.
        private readonly List<Routed> _routed = new List<Routed>();

        // Under _gate: the session number on which the file connection was refused as missing (404, 400): no routing on it.
        private long _routeMissingSession = -1;

        /// <summary>
        /// The most routed values waiting: they left the file queue, so they get its bound (CollectorOptions.MaxQueueSize);
        /// past it the oldest that has not begun is dropped and counted, as the file queue drops its oldest.
        /// </summary>
        internal int MaxRouted { get; set; } = 20000;

        /// <summary>
        /// An ordinary file value above the threshold (ADR-0022 point 21): sent on the file connection, not secure, in the
        /// order sent and ahead of the files of the large-file API. False when the route is known to be missing on this
        /// session (or the collector is not running): the caller sends the value the ordinary way. Never throws.
        /// </summary>
        internal bool Route(FileSensorValue value)
        {
            // Nothing goes on the file connection before the next Start after a 1008: the ordinary path takes it.
            if (value == null || _transport == null || _keyRefused != null)
                return false;

            Routed evicted = null;

            lock (_gate)
            {
                if (_disposed || !_running || _routeMissingSession == _transport.SessionNumber)
                    return false;

                if (_routed.Count >= MaxRouted)
                {
                    evicted = _routed.FirstOrDefault(r => !r.Began);

                    if (evicted != null)
                        _routed.Remove(evicted);
                }

                _routed.Add(new Routed(value, _timings.Now));
                EnsureThread();
                _wake.Set();
            }

            if (evicted != null)
                CountDroppedFile(evicted, $"more than {MaxRouted} file values waited for the file connection");

            return true;
        }

        /// <summary>For tests: routed sends waiting.</summary>
        internal int RoutedCount
        {
            get
            {
                lock (_gate)
                    return _routed.Count;
            }
        }

        // A routed value that can go now (no wait set, its sensor's registration acknowledged): only such a one preempts a
        // large file in progress. On the uploader's thread.
        private bool RoutedWaiting()
        {
            List<Routed> routed;

            lock (_gate)
                routed = _routed.ToList();

            var now = _timings.Now;

            return routed.Any(r => !(_notBefore.TryGetValue(r.Key, out var at) && at > now) && _transport.RegistrationOf(r.SensorPath) == true);
        }

        // The file connection is missing on this session: what waits goes back to the ordinary path (a list frame where it
        // fits, else dropped and counted, as today).
        private void RouteMissing(long session)
        {
            List<Routed> back;

            // A value whose upload began stays: it may already be stored (an answer that was lost), and the next session's
            // begin says so; given back, it would be stored twice.
            lock (_gate)
            {
                _routeMissingSession = session;
                back = _routed.Where(r => !r.Began).ToList();
                _routed.RemoveAll(r => !r.Began);
            }

            foreach (var item in back)
                GiveBack(item);
        }

        private void GiveBack(Routed item)
        {
            try
            {
                if (_requeueFile != null)
                    _requeueFile(item.Value);
                else
                    CountDroppedFile(item, "there is no ordinary path for it here");
            }
            catch (Exception ex)
            {
                CountDroppedFile(item, ex.Message);
            }
        }

        private void CompleteRouted(Job job)
        {
            lock (_gate)
                _routed.Remove(job.Routed);
        }

        // A routed send that was not stored: a dropped file value, said once with its path and size and counted where a
        // dropped file value is counted (the file queue), not under "Large files".
        private void DropRouted(Job job, string reason)
        {
            bool waiting;

            lock (_gate)
                waiting = _routed.Remove(job.Routed);

            if (waiting)
                CountDroppedFile(job.Routed, reason);
        }

        private void DropAllRouted(string why)
        {
            List<Routed> dropped;

            lock (_gate)
            {
                dropped = _routed.ToList();
                _routed.Clear();
            }

            if (dropped.Count == 0)
                return;

            Report($"{dropped.Count} file value(s) larger than a quarter of a session frame were dropped: {why} before they were stored ({string.Join("; ", dropped.Take(5))}{(dropped.Count > 5 ? "; ..." : string.Empty)}).");

            try { _countDroppedFileValues?.Invoke(dropped.Count); }
            catch (Exception) { }
        }

        private void CountDroppedFile(Routed item, string reason)
        {
            Report($"Dropped {item}: {reason}.");

            try { _countDroppedFileValues?.Invoke(1); }
            catch (Exception) { }
        }
    }
}
