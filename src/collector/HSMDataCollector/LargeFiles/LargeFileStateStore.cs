using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using HSMDataCollector.Core;

namespace HSMDataCollector.LargeFiles
{
    /// <summary>One file the state remembers: delivered, or failing in a way that has a bound (#1888 design §4, §12.6).</summary>
    internal sealed class StateEntry
    {
        public string Path { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public string Modified { get; set; }
        public string Source { get; set; }

        /// <summary>The value's time the server answered, for a delivered file.</summary>
        public string Stored { get; set; }

        public int Discards { get; set; }
        public int ChecksumMismatches { get; set; }
        public int ProtocolCloses { get; set; }

        /// <summary>Why the collector gave the file up; null while it has not.</summary>
        public string GivenUp { get; set; }

        internal bool Is(string path, FileFacts facts) =>
            string.Equals(Path, path, StringComparison.Ordinal) && Size == facts.Size
            && string.Equals(Modified, facts.ModifiedText, StringComparison.Ordinal) && string.Equals(Source, facts.Source, StringComparison.Ordinal);

        internal static StateEntry For(string path, FileFacts facts) =>
            new StateEntry { Path = path, Name = facts.Name, Size = facts.Size, Modified = facts.ModifiedText, Source = facts.Source };
    }


    internal sealed class StateDocument
    {
        public int Version { get; set; } = 1;
        public string Folder { get; set; }
        public string Mask { get; set; }
        public string Sensor { get; set; }
        public List<StateEntry> Delivered { get; set; } = new List<StateEntry>();
        public List<StateEntry> Failing { get; set; } = new List<StateEntry>();
    }


    /// <summary>The record of an upload in progress: enough to continue it, or to discard it, after a restart.</summary>
    internal sealed class UploadRecord
    {
        public int Version { get; set; } = 1;
        public string Path { get; set; }
        public string Sensor { get; set; }
        public string Upload { get; set; }
        public string Name { get; set; }
        public long Size { get; set; }
        public string Modified { get; set; }
        public string Source { get; set; }
        public int PieceBytes { get; set; }
        public long Pieces { get; set; }

        /// <summary>The mark sent in <c>begin</c>; part of the upload's key (another mark is another upload).</summary>
        public bool Secure { get; set; } = true;

        /// <summary>The same file, as first begun: name, size, modification time and identity.</summary>
        internal bool IsFile(FileFacts facts) =>
            Size == facts.Size && string.Equals(Name, facts.Name, StringComparison.Ordinal)
            && string.Equals(Modified, facts.ModifiedText, StringComparison.Ordinal) && string.Equals(Source, facts.Source, StringComparison.Ordinal);

        /// <summary>The same upload: the same file, piece size and mark.</summary>
        internal bool Is(FileFacts facts, int pieceBytes, bool secure) => IsFile(facts) && PieceBytes == pieceBytes && Secure == secure;
    }


    /// <summary>
    /// The collector's data folder for large files (#1888 design §4, §12.4): <c>&lt;base&gt;/HSMDataCollector/&lt;identity&gt;/large-files</c>.
    /// Each state file has a lock file next to it, held with <see cref="FileShare.None"/> while the state is in use, so two
    /// processes never write one state; the state itself is written whole to a temporary file and renamed over the old one,
    /// so a kill leaves the old or the new state, never half of one. A store whose folder cannot be made works without
    /// files: it remembers nothing across restarts and says so once. Not thread-safe by itself: the uploader's thread uses it,
    /// and a held lock belongs to one user.
    /// </summary>
    internal sealed class LargeFileStateStore
    {
        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private readonly Action<string> _report;

        private readonly string _root;
        private readonly object _made = new object();
        private readonly SaidOnce _said = new SaidOnce();
        private bool _tried;
        private string _usable;
        private string _unusable;

        internal LargeFileStateStore(string root, Action<string> report)
        {
            _report = report;
            _root = root;
        }

        /// <summary>
        /// The folder of the state files, made on first use (a collector that only routes ordinary file values never makes
        /// it); null when there is none or it cannot be made.
        /// </summary>
        internal string Root
        {
            get
            {
                lock (_made)
                {
                    if (_tried)
                        return _usable;

                    _tried = true;

                    if (_root == null)
                    {
                        _unusable = "the collector has no data folder for large files (CollectorOptions.DataFolder)";
                        return null;
                    }

                    // Made and written once a run (Reprobe at each Start): a folder this account cannot write is told apart from a
                    // lock another process holds.
                    try
                    {
                        Directory.CreateDirectory(_root);

                        var probe = System.IO.Path.Combine(_root, "probe-" + Guid.NewGuid().ToString("N") + ".tmp");

                        File.WriteAllBytes(probe, new byte[1]);
                        _usable = _root;

                        // Written: the folder is usable. A probe that cannot be deleted (an antivirus holding it) is said, no more.
                        try { File.Delete(probe); }
                        catch (Exception ex) { Say($"The collector could not delete its probe file {probe} ({ex.Message}); the data folder is used all the same."); }
                    }
                    catch (Exception ex)
                    {
                        _unusable = $"the collector cannot write its data folder {_root} ({ex.Message})";
                        Say($"Large files are not sent: {_unusable}. Set CollectorOptions.DataFolder to a folder this account can write.");
                    }

                    return _usable;
                }
            }
        }

        // ---- Where ----------------------------------------------------------------------------------------------------

        /// <summary>
        /// The folder of this collector's large file state: <c>DataFolder</c> (or the user's local application data, or the
        /// temporary folder when the user has none) / <c>HSMDataCollector</c> / identity / <c>large-files</c>.
        /// </summary>
        internal static string ResolveRoot(CollectorOptions options, out string note)
        {
            try
            {
                return ResolveRootUnchecked(options, out note);
            }
            catch (Exception ex)
            {
                note = $"The data folder of large files cannot be found from CollectorOptions.DataFolder '{options.DataFolder}' ({ex.Message}): watched folders and handed-over large files are unavailable (file values above a quarter of the session frame do not use it and still go on the file connection).";
                return null;
            }
        }

        private static string ResolveRootUnchecked(CollectorOptions options, out string note)
        {
            note = null;

            var baseFolder = options.DataFolder;

            if (string.IsNullOrWhiteSpace(baseFolder))
            {
                string local = null;

                try { local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData); }
                catch (Exception) { }

                if (string.IsNullOrEmpty(local))
                {
                    local = System.IO.Path.GetTempPath();
                    note = $"The user of this process has no local application data folder (a service without a loaded profile, or HOME unset): the state of large files is kept under the temporary folder {local}, which the system may clean; set CollectorOptions.DataFolder to keep it.";
                }

                baseFolder = System.IO.Path.Combine(local, "HSMDataCollector");
            }

            return System.IO.Path.Combine(System.IO.Path.GetFullPath(baseFolder), Identity(options), "large-files");
        }

        /// <summary>
        /// The collector's identity: the first 16 hex digits of the SHA-256 of the server, the port, the access key, the
        /// computer, the module and the executable's path. Two applications of one user, or one application sending to two
        /// products, never share a state; two processes of one executable with the same options share it and meet the locks.
        /// </summary>
        internal static string Identity(CollectorOptions options)
        {
            string process = string.Empty;

            try
            {
                using (var current = Process.GetCurrentProcess())
                    process = current.MainModule?.FileName ?? string.Empty;
            }
            catch (Exception) { }

            return Key(string.Join("|", options.ServerAddress ?? string.Empty, options.Port.ToString(CultureInfo.InvariantCulture),
                                   options.AccessKey ?? string.Empty, options.ComputerName ?? string.Empty, options.Module ?? string.Empty, process));
        }

        internal static string Key(string text)
        {
            using (var sha = SHA256.Create())
                return PieceFrame.Hex(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Substring(0, 16);
        }

        /// <summary>A full path as a key: lower-case on Windows, where paths are not case-sensitive.</summary>
        internal static string PathKey(string fullPath) => SourceFile.IsWindows ? fullPath.ToLowerInvariant() : fullPath;

        internal static string WatchKey(string sensorPath, string folder, string mask) => "watch-" + Key($"{PathKey(folder)}|{mask}|{sensorPath}");

        internal static string SentKey(string sensorPath) => "sent-" + Key(sensorPath);

        internal static string UploadKey(string sensorPath, string filePath) => "upload-" + Key($"{sensorPath}|{PathKey(filePath)}");


        // ---- Locks ------------------------------------------------------------------------------------------------------

        /// <summary>
        /// Takes the lock of state <paramref name="key"/> (its <c>.lock</c> file, held open with no sharing) within
        /// <paramref name="wait"/>. Null when another process holds it; a lock that needs no file (no data folder) otherwise.
        /// </summary>
        internal StateLock TryLock(string key, TimeSpan wait)
        {
            if (Root == null)
                return new StateLock(null);

            var path = System.IO.Path.Combine(Root, key + ".lock");
            var until = Stopwatch.StartNew();

            while (true)
            {
                try
                {
                    // On Windows deleted when it is let go (nobody else can open it meanwhile: no sharing, no delete sharing), so
                    // locks of files long gone do not pile up. Elsewhere the lock is advisory and a delete on close could race a
                    // waiter that opened the old file: the lock file is kept.
                    return new StateLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1,
                                                        SourceFile.IsWindows ? FileOptions.DeleteOnClose : FileOptions.None));
                }
                catch (IOException) when (until.Elapsed < wait)
                {
                    Thread.Sleep(50);
                }
                catch (IOException)
                {
                    return null;
                }
                catch (UnauthorizedAccessException) when (until.Elapsed < wait)
                {
                    // Access denied on a lock file is someone else's: a lock file another handle is deleting, or one another
                    // account made. Waited for like a held lock.
                    Thread.Sleep(50);
                }
                catch (UnauthorizedAccessException ex)
                {
                    Say($"The collector cannot take the lock {path} ({ex.Message}): it treats that state as held by another process and does not write it.");
                    return null;
                }
            }
        }


        // ---- State documents ------------------------------------------------------------------------------------------

        internal StateDocument ReadState(string key) => Read<StateDocument>(key + ".json", d => d.Version == 1) ?? new StateDocument();

        internal void WriteState(string key, StateDocument document) => Write(key + ".json", document);

        internal UploadRecord ReadUpload(string key) => Read<UploadRecord>(key + ".json", r => r.Version == 1 && r.Upload != null && r.PieceBytes > 0);

        internal void WriteUpload(string key, UploadRecord record) => Write(key + ".json", record);

        /// <summary>The next use of the folder probes it again (each Start of the collector).</summary>
        internal void Reprobe()
        {
            lock (_made)
            {
                _tried = false;
                _usable = null;
                _unusable = null;
            }
        }

        /// <summary>
        /// Whether the data folder can be used (made and written); probes it on the first call. False with the reason when it
        /// cannot: large files of the large-file API are then Unavailable.
        /// </summary>
        internal bool IsUsable(out string reason)
        {
            var usable = Root != null;

            reason = usable ? null : _unusable;
            return usable;
        }

        /// <summary>The keys of every upload record in the data folder, for the sweep of records whose file changed or went.</summary>
        internal IReadOnlyList<string> UploadKeys()
        {
            // Not made yet: no records (and the sweep does not make it).
            if (_root == null || !Directory.Exists(_root) || Root == null)
                return new string[0];

            try
            {
                return Directory.GetFiles(Root, "upload-*.json").Select(f => System.IO.Path.GetFileNameWithoutExtension(f)).ToList();
            }
            catch (Exception)
            {
                return new string[0];
            }
        }

        /// <summary>Forgets an upload in progress: its record and its digests.</summary>
        internal void DeleteUpload(string key)
        {
            if (Root == null)
                return;

            TryDelete(System.IO.Path.Combine(Root, key + ".json"));
            TryDelete(System.IO.Path.Combine(Root, key + ".digests"));
        }

        /// <summary>
        /// The piece digests of an upload, indexed by piece number (32 bytes each; all zeros = not known): an array of
        /// <paramref name="pieces"/> × 32 bytes, read from the data folder when a list of that length is there.
        /// </summary>
        internal byte[] ReadDigests(string key, long pieces)
        {
            var digests = new byte[checked((int)(pieces * 32))];

            if (Root == null || pieces == 0)
                return digests;

            try
            {
                var path = System.IO.Path.Combine(Root, key + ".digests");

                if (File.Exists(path))
                    using (var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                        if (file.Length == digests.Length)
                        {
                            var read = 0;

                            while (read < digests.Length)
                            {
                                var n = file.Read(digests, read, digests.Length - read);

                                if (n == 0)
                                    break;

                                read += n;
                            }
                        }
            }
            catch (Exception ex)
            {
                Say($"The collector cannot read the piece digests of an upload in progress ({ex.Message}); it reads those pieces of the file again.");
                Array.Clear(digests, 0, digests.Length);
            }

            return digests;
        }

        /// <summary>Writes the digest of piece <paramref name="number"/> at its place in the list and flushes it.</summary>
        internal void WriteDigest(string key, long pieces, long number, byte[] digest)
        {
            if (Root == null)
                return;

            try
            {
                using (var file = new FileStream(System.IO.Path.Combine(Root, key + ".digests"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read))
                {
                    if (file.Length != pieces * 32)
                        file.SetLength(pieces * 32);

                    file.Position = number * 32;
                    file.Write(digest, 0, 32);
                    file.Flush(true);
                }
            }
            catch (Exception ex)
            {
                Say($"The collector cannot keep the piece digests of an upload in progress ({ex.Message}); after a restart it reads the sent pieces of the file again.");
            }
        }

        private T Read<T>(string name, Func<T, bool> valid) where T : class
        {
            if (Root == null)
                return null;

            var path = System.IO.Path.Combine(Root, name);

            try
            {
                if (!File.Exists(path))
                    return null;

                var text = File.ReadAllText(path, Encoding.UTF8);
                T value = null;

                try { value = JsonSerializer.Deserialize<T>(text, Json); }
                catch (JsonException) { }

                if (value != null && valid(value))
                    return value;

                var aside = $"{path}.corrupt-{DateTime.UtcNow:yyyyMMddHHmmss}";

                try { File.Move(path, aside); }
                catch (Exception) { TryDelete(path); }

                Say($"The large file state {path} is not readable; it was put aside as {aside} and the collector starts that state empty: a kept file may be offered again (the server answers that it has it, as long as it has it).");
                return null;
            }
            catch (Exception ex)
            {
                Say($"The collector cannot read its large file state {path} ({ex.Message}); it goes on without it.");
                return null;
            }
        }

        private void Write<T>(string name, T value)
        {
            if (Root == null)
                return;

            var path = System.IO.Path.Combine(Root, name);

            // A name of its own: two writers never rename each other's half-written file.
            var temporary = path + ".tmp-" + Guid.NewGuid().ToString("N");

            try
            {
                var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Json);

                using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }

                if (File.Exists(path))
                    File.Replace(temporary, path, null);
                else
                    File.Move(temporary, path);
            }
            catch (Exception ex)
            {
                Say($"The collector cannot write its large file state {path} ({ex.Message}); after a restart it may offer a kept file again.");
                TryDelete(temporary);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception) { }
        }

        // Said once per text: a failure repeated on every try is one report.
        private void Say(string text)
        {
            if (!_said.First(text))
                return;

            try { _report?.Invoke(text); }
            catch (Exception) { }
        }
    }


    /// <summary>A held lock of a state; disposing it lets it go.</summary>
    internal sealed class StateLock : IDisposable
    {
        private FileStream _file;

        internal StateLock(FileStream file) => _file = file;

        public void Dispose()
        {
            try { Interlocked.Exchange(ref _file, null)?.Dispose(); }
            catch (Exception) { }
        }
    }
}
