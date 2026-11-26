using System;
using System.Threading;
using System.Threading.Tasks;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

namespace HSMDataCollector.Core
{
    /// <summary>
    /// Optional collector capability: large file values (ADR-0022). A large file is a value of a File sensor whose content
    /// travels in pieces, compressed, on the file connection of the collector session and is kept on the server's disk; it
    /// can be as large as the server allows (64 GB by design). Kept apart from <see cref="IDataCollector"/> so external
    /// implementations of that interface stay compatible; <see cref="DataCollector"/> implements it.
    /// <para>
    /// Large files need the collector session transport (<see cref="CollectorOptions.Transport"/> =
    /// <see cref="CollectorTransport.Session"/>, the default). One file is uploaded at a time, on one background thread of
    /// lowered priority, at most <see cref="LargeFileOptions.MaxBytesPerSecond"/>; the caller's thread never reads a file.
    /// Uploads run between Start and Stop; Stop and Dispose do not wait for one: it is cut and continues after the next Start
    /// from the piece the server has. Every member may be called from any thread and in any state of the collector.
    /// </para>
    /// </summary>
    public interface ILargeFileCollector
    {
        /// <summary>
        /// Watches a folder (not its subfolders) and uploads every file whose name matches <paramref name="mask"/> as a large
        /// value of the File sensor at <paramref name="sensorPath"/>, once the file has settled: its size and modification
        /// time unchanged for <see cref="LargeFileOptions.SettleTime"/> and, on Windows, no process holding it open for
        /// writing. Files already in the folder are taken by the same rule, oldest first. Symbolic links and reparse points
        /// are skipped. By default the source file is deleted after the server confirmed it stored the whole file
        /// (<see cref="LargeFileOptions.DeleteAfterUpload"/>).
        /// <para>
        /// Throws <see cref="ArgumentException"/> (or <see cref="ArgumentNullException"/>,
        /// <see cref="ArgumentOutOfRangeException"/>) only for a mistake of the call: a null or empty sensor path, folder or
        /// mask; a mask with a directory separator; a sensor path with a backslash or a tab; options out of range; or a
        /// second live watch of the same sensor path, folder and mask. Never throws for a condition of the environment: a
        /// missing or unreadable folder, a collector on the HTTP transport, a server without large files, a disposed
        /// collector. Those are said once through the collector's errors and in <see cref="ILargeFileWatch.State"/>.
        /// </para>
        /// </summary>
        /// <param name="sensorPath">The File sensor's path, placed as <see cref="IDataCollector.CreateFileSensor(string, FileSensorOptions)"/>
        /// places it (<see cref="SensorOptions.SensorLocation"/> of <see cref="LargeFileOptions.SensorOptions"/>; under the
        /// computer and module by default).</param>
        /// <param name="folder">The folder to watch; a relative path is taken from the current directory at the call.</param>
        /// <param name="mask">A file name mask: <c>*</c> any run of characters, <c>?</c> one character, the whole name
        /// matched; case-insensitive on Windows (ASCII letters), exact elsewhere. For example <c>*.dmp</c>.</param>
        /// <param name="options">Null takes the defaults.</param>
        /// <returns>The watch; dispose it to stop watching.</returns>
        ILargeFileWatch WatchLargeFiles(string sensorPath, string folder, string mask, LargeFileOptions options = null);

        /// <summary>
        /// Uploads one file as a large value of the File sensor at <paramref name="sensorPath"/>. The caller says the file is
        /// complete: there is no settle time, but on Windows a file still open for writing waits until it is not.
        /// Handed-over files go before the files of watched folders, in the order they were handed over.
        /// <para>
        /// The task completes when the file is settled: <see cref="LargeFileOutcome.Stored"/> (the source deleted unless
        /// <see cref="LargeFileOptions.DeleteAfterUpload"/> is false), refused for good, given up after repeated failures,
        /// or missing. It does not complete at Stop: the upload continues after the next Start. It completes
        /// <see cref="LargeFileOutcome.Cancelled"/> when <paramref name="token"/> fires or the collector is disposed, and
        /// <see cref="LargeFileOutcome.Unavailable"/> at once on a collector that cannot send large files. The task never
        /// faults. The same file handed over again while it is pending gets the same task.
        /// </para>
        /// <para>Throws only for the argument mistakes <see cref="WatchLargeFiles"/> names (a null or empty file path too).</para>
        /// </summary>
        Task<LargeFileResult> SendLargeFileAsync(string sensorPath, string filePath, LargeFileOptions options = null, CancellationToken token = default);
    }


    public static class LargeFileExtensions
    {
        /// <summary>
        /// Watches a folder for large files on a collector that has the <see cref="ILargeFileCollector"/> capability (every
        /// <see cref="DataCollector"/>). See <see cref="ILargeFileCollector.WatchLargeFiles"/>.
        /// </summary>
        /// <exception cref="NotSupportedException"><paramref name="collector"/> is another implementation of <see cref="IDataCollector"/>.</exception>
        public static ILargeFileWatch WatchLargeFiles(this IDataCollector collector, string sensorPath, string folder, string mask, LargeFileOptions options = null) =>
            Capability(collector).WatchLargeFiles(sensorPath, folder, mask, options);

        /// <summary>
        /// Uploads one large file on a collector that has the <see cref="ILargeFileCollector"/> capability (every
        /// <see cref="DataCollector"/>). See <see cref="ILargeFileCollector.SendLargeFileAsync"/>.
        /// </summary>
        /// <exception cref="NotSupportedException"><paramref name="collector"/> is another implementation of <see cref="IDataCollector"/>.</exception>
        public static Task<LargeFileResult> SendLargeFileAsync(this IDataCollector collector, string sensorPath, string filePath, LargeFileOptions options = null, CancellationToken token = default) =>
            Capability(collector).SendLargeFileAsync(sensorPath, filePath, options, token);

        private static ILargeFileCollector Capability(IDataCollector collector)
        {
            if (collector == null)
                throw new ArgumentNullException(nameof(collector));

            if (collector is ILargeFileCollector files)
                return files;

            throw new NotSupportedException($"{collector.GetType().FullName} does not implement {nameof(ILargeFileCollector)}: large files need the DataCollector of this package.");
        }
    }
}
