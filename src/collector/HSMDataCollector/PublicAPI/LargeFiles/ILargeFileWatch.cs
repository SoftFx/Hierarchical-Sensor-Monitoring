using System;

namespace HSMDataCollector.PublicInterface
{
    /// <summary>The state of a watched folder of large files (ADR-0022).</summary>
    public enum LargeFileWatchState : byte
    {
        /// <summary>
        /// The folder is polled while the collector runs (and from the next Start while it is stopped); its settled files
        /// are uploaded one at a time. A missing or unreadable folder keeps the watch active: it is said once, and the
        /// files are taken when the folder comes back.
        /// </summary>
        Active = 0,

        /// <summary>
        /// This collector cannot send large files: it sends over HTTP, was given a custom <c>DataSender</c>, or uses the
        /// session of another collector. Nothing of the watch is sent for the life of the collector.
        /// <see cref="ILargeFileWatch.StateReason"/> says why.
        /// </summary>
        Unavailable = 1,

        /// <summary>
        /// The server refused the registration of the File sensor (a path it does not accept, another kind of sensor
        /// there, no permission to write). Nothing of the watch is sent until the collector's next Start, which asks
        /// again. <see cref="ILargeFileWatch.StateReason"/> carries the server's words.
        /// </summary>
        Refused = 2,

        /// <summary>The watch was disposed, or was made on a disposed collector. Nothing of it is sent.</summary>
        Disposed = 3,
    }


    /// <summary>
    /// A folder watched for large files (<see cref="Core.ILargeFileCollector.WatchLargeFiles"/>). Every member may be called
    /// from any thread and in any state of the collector; none throws. Dispose it to stop watching: an upload of it in
    /// progress is cut, and its part stays on the server for a continue when the folder is watched again.
    /// </summary>
    public interface ILargeFileWatch : IDisposable
    {
        /// <summary>The File sensor's path as given to the call.</summary>
        string SensorPath { get; }

        /// <summary>The watched folder, as a full path.</summary>
        string Folder { get; }

        /// <summary>The file name mask: <c>*</c> any run of characters, <c>?</c> one character.</summary>
        string Mask { get; }

        /// <summary>The state of the watch; see <see cref="LargeFileWatchState"/>.</summary>
        LargeFileWatchState State { get; }

        /// <summary>Why the watch is <see cref="LargeFileWatchState.Unavailable"/> or <see cref="LargeFileWatchState.Refused"/>, or the
        /// last problem the watch said (a missing folder, a file refused by the server); null when there is none.</summary>
        string StateReason { get; }

        /// <summary>Files of this watch the server stored, since the watch was made.</summary>
        long FilesStored { get; }

        /// <summary>
        /// Files of this watch that were not sent: refused by the server for good, given up after repeated failures, or
        /// unreadable. Each file is counted once per run of the collector. They are also counted in the collector's
        /// <c>Queue overflow</c> sensor under the queue <c>Large files</c> and said through the collector's errors.
        /// </summary>
        long FilesNotSent { get; }
    }
}
