using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>The File sensor a watch or a handed-over file sends to, and how its registration stands.</summary>
    internal sealed class LargeFileSensor
    {
        internal LargeFileSensor(string givenPath, string registeredPath, HSMSensorDataObjects.SensorRequests.AddOrUpdateSensorRequest request)
        {
            GivenPath = givenPath;
            Path = registeredPath;
            Request = request;
        }

        internal string GivenPath { get; }

        /// <summary>The path of the registration and of <c>begin</c>.</summary>
        internal string Path { get; }

        internal HSMSensorDataObjects.SensorRequests.AddOrUpdateSensorRequest Request { get; }

        /// <summary>The session number its registration was accepted on; -1 when it has not been in this run.</summary>
        internal long RegisteredOn { get; set; } = -1;

        /// <summary>The server's refusal of the registration in this run; null when there is none.</summary>
        internal string Refused { get; set; }
    }


    /// <summary>What the watch knows of one file between scans.</summary>
    internal sealed class Observation
    {
        internal long Size;
        internal DateTime Modified;
        internal TimeSpan UnchangedSince;

        /// <summary>The identity checked for a delivered or given-up entry of these size and time: null = not checked yet.</summary>
        internal bool? KnownEntryConfirmed;
    }


    /// <summary>
    /// A watched folder (#1888 design §2): polled by the uploader's thread, never by a thread of the caller. Holds the lock
    /// of its state file for its life, so two processes never watch one folder with one mask and sensor at once.
    /// </summary>
    internal sealed class LargeFileWatch : ILargeFileWatch
    {
        private readonly LargeFileService _service;
        private long _stored;
        private long _notSent;
        private int _state;
        private string _reason;

        internal LargeFileWatch(LargeFileService service, LargeFileSensor sensor, string givenPath, string folder, string mask, LargeFileOptions options, LargeFileWatchState state, string reason)
        {
            _service = service;
            Sensor = sensor;
            SensorPath = givenPath;
            Folder = folder;
            Mask = mask;
            Matcher = FileNameMask.ForThisSystem(mask);
            Options = options;
            _state = (int)state;
            _reason = reason;
            StateKey = sensor == null ? null : LargeFileStateStore.WatchKey(sensor.Path, folder, mask);
        }

        public string SensorPath { get; }

        public string Folder { get; }

        public string Mask { get; }

        public LargeFileWatchState State => (LargeFileWatchState)Volatile.Read(ref _state);

        public string StateReason => Volatile.Read(ref _reason);

        public long FilesStored => Interlocked.Read(ref _stored);

        public long FilesNotSent => Interlocked.Read(ref _notSent);

        internal LargeFileSensor Sensor { get; }

        internal FileNameMask Matcher { get; }

        internal LargeFileOptions Options { get; }

        internal string StateKey { get; }

        /// <summary>Its order of creation: a file that matches two watches is the earlier one's.</summary>
        internal long Order { get; set; }

        // ---- Owned by the uploader's thread -----------------------------------------------------------------------------

        internal readonly Dictionary<string, Observation> Observations = new Dictionary<string, Observation>(StringComparer.Ordinal);

        internal StateLock Lock;
        internal StateDocument Document;
        internal bool LockHeldElsewhereSaid;
        internal TimeSpan NextScan;
        internal long GivenUpReportedRun = -1;
        internal string FolderProblem;

        internal bool IsActive => State == LargeFileWatchState.Active;

        internal void CountStored() => Interlocked.Increment(ref _stored);

        internal void CountNotSent() => Interlocked.Increment(ref _notSent);

        internal void SetReason(string reason) => Volatile.Write(ref _reason, reason);

        internal void MoveTo(LargeFileWatchState state, string reason)
        {
            Volatile.Write(ref _reason, reason);
            Volatile.Write(ref _state, (int)state);
        }

        /// <summary>Back to active at a new Start, after a refusal of its registration in the last run.</summary>
        internal void Reactivate()
        {
            if (Interlocked.CompareExchange(ref _state, (int)LargeFileWatchState.Active, (int)LargeFileWatchState.Refused) == (int)LargeFileWatchState.Refused)
                Volatile.Write(ref _reason, null);
        }

        internal bool MarkDisposed() => Interlocked.Exchange(ref _state, (int)LargeFileWatchState.Disposed) != (int)LargeFileWatchState.Disposed;

        public void Dispose()
        {
            try { _service?.Unwatch(this); }
            catch (Exception) { }
        }

        public override string ToString() => $"{Folder}{System.IO.Path.DirectorySeparatorChar}{Mask} -> {SensorPath}";
    }
}
