# DataCollector Large Files

> Large files need HSMServer 3.46.0 or later and one of: the managed collector HSMDataCollector `4.2.0-preview` (NuGet prerelease), the native collector 0.16.0 or the C++ wrapper 2.1.0. Earlier servers and collectors have no large files.

This page is for a developer whose application sends big files to HSM: crash dumps above all, but any file up to the server's limit (64 GiB by default). What the site shows, the dump password of a product and the download are on [Large Files](Large-Files). All kinds of sensors are compared on [Sensor Types](Sensor-types).

In short: you give the collector a folder to watch, or hand it one file. The collector waits until a file has stopped changing, uploads it in compressed pieces on a connection of its own, at most 10 MB/s by default, continues where it stopped after a lost connection or a restart, and deletes the source only after the server has stored and checked the whole file. On the server the file becomes a value of a **File sensor**: a zip, encrypted under the product's dump password for a **secure** file (the default, meant for dumps), or without a password for an ordinary one. Nothing ever throws because the network or the server is not there: files that cannot be sent are reported and counted.

---

## What you need

| Part | Version |
|---|---|
| Server | 3.46.0 or later, with collector sessions and file connections on (both are on by default) |
| Managed collector (C#) | HSMDataCollector `4.2.0-preview` (NuGet prerelease), .NET 6+ or .NET Framework 4.7.2+ |
| Native collector (C, C++) | 0.16.0 (the vcpkg port `hsm-collector` 0.16.0 with the feature `tables`), built with `HSM_COLLECTOR_HTTP=ON` and `HSM_COLLECTOR_TABLES=ON`, and libcurl 8.14.0 or later with WebSocket support |
| Legacy C++ wrapper | HSMCppWrapper 2.1.0 (it carries native collector 0.16.0) |
| Transport | **the collector session** (the default of the managed collector since 4.0.0 and of the wrapper since 2.0.0; the native collector needs `UseSessionTransport()`) |

A file travels on a third WebSocket of the collector session, the **file connection**, which the collector opens by itself when it has a file to send and closes when it has none. **A collector on the HTTP transport cannot send large files**: its watches are `Unavailable` and a handed-over file completes `Unavailable` at once.

### Two ways a file becomes a large value

1. **The large file API** (this page): `WatchLargeFiles` and `SendLargeFileAsync` in C#, `hsm_collector_watch_large_files` and `hsm_collector_send_large_file` in C. The file is read from the disk piece by piece; it can be as large as the server allows. Secure by default.
2. **An ordinary `SendFile`** whose content is larger than a quarter of the session's frame limit (1 MiB with the server's default 4 MiB frame). On a session it takes the same path by itself, as a large value that is **not secure**. Below that size nothing changes. See [SendFile above the threshold](#sendfile-above-the-threshold).

---

## The managed collector (C#)

### Watching a folder

```csharp
using HSMDataCollector.Core;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

var collector = new DataCollector(new CollectorOptions
{
    ServerAddress = "https://hsm.example.com",
    AccessKey = "<access key of the product>",
});

ILargeFileWatch dumps = collector.WatchLargeFiles("crashes/dumps", @"D:\MyApp\Dumps", "*.dmp", new LargeFileOptions
{
    SensorOptions = new FileSensorOptions { Description = "Crash dumps of MyApp" },
});

await collector.Start();

// ... later, from any thread:
Console.WriteLine($"{dumps.State} {dumps.StateReason}: stored {dumps.FilesStored}, not sent {dumps.FilesNotSent}");

dumps.Dispose(); // stops watching
```

- **The sensor path** is the File sensor's, placed as `CreateFileSensor` places it: under `ComputerName/Module` by default, at the product root with `SensorLocation.Product` in `SensorOptions`. The collector registers the sensor by itself. A backslash or a tab in the path is a mistake of the call.
- **The folder**: its top level only, not its subfolders. A relative path is taken from the current directory at the call. A folder that does not exist yet or cannot be read is said once in the collector's errors; the watch stays `Active` and takes the files when the folder can be read.
- **The mask** matches the whole file name: `*` any run of characters, `?` one character. On Windows the case of ASCII letters is ignored, elsewhere the match is exact. `[`, `]`, `;` and directory separators are refused at the call. Both collectors match a name the same way.
- You may watch before or after `Start()`. Nothing of the feature exists (no thread, no connection, no data folder) until the first watch, hand-over or `SendFile` value above the threshold (which never makes the data folder).
- **What throws.** `WatchLargeFiles` throws `ArgumentException` (or `ArgumentNullException`, `ArgumentOutOfRangeException`) only for a mistake of the call: an empty sensor path, folder or mask; a mask with a separator, `[`, `]` or `;`; a path the system cannot take; a sensor path with a backslash or a tab; options out of range; a second live watch of the same sensor path, folder and mask (dispose the first). It **never** throws because of the environment: a missing folder, the HTTP transport, a server without large files or a disposed collector give a watch with that state.
- `WatchLargeFiles` and `SendLargeFileAsync` are on `DataCollector`, on the interface `ILargeFileCollector`, and as extension methods on `IDataCollector` (`LargeFileExtensions`, which throw `NotSupportedException` for another implementation of `IDataCollector`).

Two watches whose masks overlap on one folder take each file once, by the watch created first. One sensor path may have several watches (several folders); the first registration of a path stands, and another one with other options is said once and not sent.

### When a file is taken

A watched file is taken when **its size and modification time have not changed for the settle time** (`SettleTime`, 60 seconds by default), as the collector's own scans observe them. The folder is polled every quarter of the settle time, at most every 10 seconds and at least every 250 ms. Files that are already there when the watch starts wait one settle time like any other: an old modification time is not trusted.

On Windows the collector then opens the file so that nobody else may write or delete it while it is sent. If another program still has it open for writing, the open fails and the file waits another settle time. A file another program keeps open (for writing, or without sharing it at all) for 10 tries in a row is said and counted once, and is still taken when that program lets it go.

Order: handed-over files first, in the order they were handed over; then the settled files of the watches, oldest modification time first, then by name. **One file is uploaded at a time** per collector.

### Handing over one file

When the application knows that a file is complete, it hands it over. There is no settle time (on Windows a file still open for writing waits until it is not).

```csharp
LargeFileResult result = await collector.SendLargeFileAsync("crashes/dumps", @"D:\MyApp\Dumps\on-request.dmp",
    new LargeFileOptions { DeleteAfterUpload = false }, cancellationToken);

if (result.Outcome == LargeFileOutcome.Stored)
    Console.WriteLine($"Stored, value time {result.StoredTime:O}, source deleted: {result.SourceDeleted}");
else
    Console.WriteLine(result); // for example "Refused: ..." or "Unavailable: ..."
```

- The task **never faults**. It completes when the file is settled one way or another; **it does not complete at `Stop`**: the upload continues after the next `Start`.
- The same file handed over again while it is pending returns the same task.
- The cancellation token takes the file out of the queue (`Cancelled`); an upload in progress is cut, and its part stays on the server, so the same file handed over later continues from there.
- A handed-over file is held in memory: after a restart of the process, hand it over again, and the server continues it from the piece it has.
- `SendLargeFileAsync` throws only for the mistakes of the call that `WatchLargeFiles` names, and for an empty file path.

`LargeFileResult`:

| Member | Meaning |
|---|---|
| `Outcome` | what became of the file (below) |
| `Reason` | why it was not stored, in words for a log; null when stored (or the reason a stored source was not deleted) |
| `StoredTime` | the time of the value the server made of the file (UTC); null when the server answered that it had the file already |
| `SourceDeleted` | true when the collector deleted the source after the server stored it |

`LargeFileOutcome`:

| Outcome | Meaning |
|---|---|
| `Stored` | The server stored the whole file and checked it; it is a value of the sensor now. The source was deleted unless `DeleteAfterUpload` is false. |
| `Refused` | Refused for good: larger than the server takes, a sensor that cannot take it, the server refused the sensor's registration. The file is left where it is and not offered again before the collector's next `Start`. |
| `GivenUp` | Failures a repeat does not cure (the server discarded the upload three times, ten checksum mismatches, three protocol errors). The file is left and offered again only when it changes. |
| `Missing` | The file was not there, was a symbolic link or a reparse point, or could not be opened for reading. |
| `Unavailable` | This collector cannot send large files; `Reason` says why. Nothing was sent. |
| `Cancelled` | The token fired, or the collector was disposed, before the file was settled. |

### The options

`LargeFileOptions` is read once, when the watch is made or the file handed over; changing the object later changes nothing.

```csharp
var options = new LargeFileOptions
{
    SettleTime = TimeSpan.FromSeconds(60), // the default; at least 1 s; watched folders only
    DeleteAfterUpload = true,              // the default
    MaxBytesPerSecond = 10_000_000,        // the default: bytes on the wire per second; 0 = no limit
    NewFileAlert = true,                   // the default
    Secure = true,                         // the default: a zip under the product's dump password
    SensorOptions = new FileSensorOptions
    {
        Description = "Crash dumps of MyApp",
        KeepHistory = TimeSpan.FromDays(90),
    },
};
```

| Option | Default | Meaning |
|---|---|---|
| `SettleTime` | 60 s | How long size and modification time must stay unchanged before a watched file is taken. At least 1 s. Not used for a handed-over file. |
| `DeleteAfterUpload` | `true` | Delete the source after the server stored and checked the whole file. `false`: the file is left, recorded as delivered in the collector's data folder and not offered again. |
| `MaxBytesPerSecond` | 10,000,000 | The speed limit (below). 0: no limit. |
| `NewFileAlert` | `true` | The File sensor is registered with the flag `DefaultAlertsOptions.LargeFiles`. A server that creates the sensor from that registration gives it the alert "New data has been received" with the message `[$product]$path new file $fileName ($fileSize) $link $fileNote`: one notification per stored file ([Large Files](Large-Files#the-alert)). A sensor that exists already gets nothing from the flag. `false`: the flag is not sent. |
| `Secure` | `true` | A secure file (below). `false`: an ordinary one. |
| `SensorOptions` | the defaults of `FileSensorOptions` | The registration of the File sensor: `Description`, `SensorLocation`, `IsComputerSensor`, `KeepHistory`, `SelfDestroy`, `TTL`, `TtlAlert`, `Alerts`, `DefaultAlertsOptions`, `EnableForGrafana`, `IsSingletonSensor`, `IsForceUpdate`, `SensorUnit`, `Statistics`. `MaxFileSizeBytes`, `DefaultFileName`, `Extension`, `IsPrioritySensor`, `AggregateData` and `PreviousPaths` do not apply to large files and are ignored (said once when set): the limit of a large file is the server's. |

A path also created with `CreateFileSensor` is allowed; the last registration sent sets the sensor, so give both the same options.

### The state of a watch

`ILargeFileWatch` has `SensorPath`, `Folder` (the full path), `Mask`, `State`, `StateReason`, `FilesStored` and `FilesNotSent`. Every member may be read from any thread; none throws.

| `State` | Meaning |
|---|---|
| `Active` | The folder is polled while the collector runs (and from the next `Start` while it is stopped). A missing or unreadable folder keeps the watch `Active`. |
| `Unavailable` | This collector cannot send large files: the HTTP transport, a custom `DataSender`, the session of another collector, or a data folder it cannot write. Nothing of the watch is sent. |
| `Refused` | The server refused the registration of the File sensor (a path it does not accept, another kind of sensor there). Nothing is sent until the collector's next `Start`, which asks again. |
| `Disposed` | The watch was disposed, or made on a disposed collector. |

`StateReason` says why a watch is `Unavailable` or `Refused`, or names the last problem it said (a missing folder, a file the server refused); null when there is none. `FilesStored` counts the files of the watch the server stored; `FilesNotSent` the files it could not send (refused, given up, unreadable, a link, held open for 10 tries), each once per run of the collector.

`StateReason` of a watch on the HTTP transport, as we saw it:

```
large files need the collector session transport, and this collector sends over HTTP (CollectorOptions.Transport)
```

### In every state of the collector

| Call | Stopped (never started) | Starting / Running | Stopping | Disposed |
|---|---|---|---|---|
| `WatchLargeFiles` | `Active`, polls after `Start` | `Active`, polls now | `Active`, polls after the next `Start` | `Disposed` |
| `SendLargeFileAsync` | queued, uploaded after `Start` | queued, uploaded now | queued, after the next `Start` | `Cancelled` at once |
| `ILargeFileWatch.Dispose` | removes the watch | removes it; its upload is cut (the part stays on the server) | as Running | nothing |

`Stop` and `Dispose` do not wait for an upload: they cut it. It continues after the next `Start` from the piece the server has. A refusal that was final in one run is forgotten at the next `Start`.

### The data folder

The collector keeps what must survive a restart on the disk: which files were delivered (in keep mode) or given up, and the upload in progress with the digests of its pieces. The folder is `CollectorOptions.DataFolder`, by default the user's local application data folder (`%LOCALAPPDATA%\HSMDataCollector` on Windows, `~/.local/share/HSMDataCollector` on Linux), and in it a subfolder named after the collector's identity (a hash of the server, the port, the access key, the computer, the module and the executable's path), then `large-files`. Two applications, or one application sending to two products, never share a state.

```csharp
var collector = new DataCollector(new CollectorOptions
{
    ServerAddress = "https://hsm.example.com",
    AccessKey = "<access key of the product>",
    DataFolder = @"D:\MyApp\hsm-collector", // a folder the account of the application can write
});
```

- A user without a local application data folder (a service without a loaded profile) gets the temporary folder, which the system may clean; this is said once. Set `DataFolder` for a service.
- The folder is made and written once (a probe file) at the first watch or hand-over after every `Start`, so a folder fixed between a `Stop` and a `Start` is used by the watches and hand-overs made after that `Start`. A watch made `Unavailable` by the folder stays so: dispose it and make it again.
- **A folder this account cannot write** makes the watches `Unavailable` and the handed-over files complete `Unavailable`, with a reason that names the folder, said once in the collector's errors. What we saw with a `DataFolder` that is a file:

  ```
  error: Large files are not sent: the collector cannot write its data folder <DataFolder>\<identity>\large-files (Cannot create '<DataFolder>' because a file or directory with the same name already exists.). Set CollectorOptions.DataFolder to a folder this account can write.
  ```

- Values of `SendFile` do not use the data folder, but while it is known that the folder cannot be written they do not take the file connection: they go the ordinary way.
- A state file in use is locked against other processes. Two processes of one executable with the same options share one state: a watch whose state another process holds says so once and waits. Each state file is written whole and renamed over the old one, so a kill leaves the old or the new state. A damaged state file is put aside (`.corrupt-<time>`), said once, and started empty; a kept file may then be offered again, and the server answers that it has it.

### Lost files and where they are reported

Every file not sent is said once through the collector's errors (the log and the **`Collector errors`** sensor, added by `AddCollectorErrors()` or `AddCollectorMonitoringSensors()`) and counted:

- in the **`Queue overflow`** sensor (node `Collector queue stats`, added by `AddQueueOverflow()`) under the queue `Large files`;
- in the watch's `FilesNotSent`, or as the outcome of a handed-over file.

That covers a file refused by the server, given up, missing or unreadable, a symbolic link, a file held open for 10 tries, and handed-over files still queued at `Dispose` ("N handed-over large file(s) were not sent: the collector was disposed before their upload ended. The files are left where they are."). A file that changed while it was sent is not a loss: it is logged and sent again when it has settled. A value of `SendFile` that took the file connection and was not stored is counted under the queue `File`, where a dropped file value is always counted.

Uploads log at Info level. What we saw for a watched dump and a `SendFile` of 2.7 MB:

```
info: The file connection is up: frame limit 4194304 bytes, piece 4173824 bytes, file limit 68719476736 bytes.
info: The file value of reports/daily (2718735 bytes) is stored on the server as a large file value.
info: The file connection ended: no file waits.
info: The file connection is up: frame limit 4194304 bytes, piece 4173824 bytes, file limit 68719476736 bytes.
info: The large file D:\try-it\dumps\app-crash.dmp is stored on the server and deleted here.
info: The file connection ended: no file waits.
```

### .NET Framework

The same API works on .NET Framework 4.7.2. With `AllowUntrustedServerCertificate` on `https`, .NET Framework cannot open a session at all, so large files are `Unavailable` there: use a trusted certificate. The package depends on `ZstdSharp.Port` (a managed zstd, no native binaries).

---

## The upload

### The source file and when it is deleted

- **Deleted only after the server confirmed** that it stored and checked the whole file, and only **the file that was sent**. On Windows the collector holds the file open for the whole upload, so nobody else can write or delete it, and deletes it through that same handle. Elsewhere it compares the size and modification time again and deletes by path.
- A file that changed after it was sent is not deleted (logged).
- With `DeleteAfterUpload = false` the file is left and recorded as delivered in the data folder, and the watch does not offer it again.
- A file that **changes during the upload** (size, modification time, or on Windows its identity) is noticed before the end: the collector asks the server to discard the part and takes the file again when it has settled, as a new upload from the start.

### The speed limit

`MaxBytesPerSecond` (10,000,000 by default, that is 10 MB/s) counts the **bytes on the wire**: each piece as it is sent, compressed, with its 88-byte header. The collector reads a piece, compresses it, waits until the piece fits the limit, then sends it at full speed. 0 means no limit. The limit counts what goes on the wire, so a dump that compresses well is read from the disk faster than that; the upload runs on one background thread of lowered priority. The limit applies to the routed `SendFile` values too (at the default).

Pieces are about 4 MiB (the server's file frame, 4 MiB by default, less a little), compressed with zstd level 3; one piece is in memory at a time.

### A restart, a lost connection, a full server disk

| What happens | What the collector does |
|---|---|
| The connection is lost (or the server does not answer in time) | A new connection after the session's backoff; the upload continues from the piece the server has. |
| The collector stops (`Stop`) | The upload is cut, and continues after the next `Start` from the server's piece; handed-over files stay queued. |
| The application ends (`Dispose`, a crash, a reboot) | The upload is cut. After the next start a watched file is found again and continues from the server's piece; a handed-over file continues when the application hands it over again. |
| The server restarts | The same as a lost connection: the upload continues from the server's piece. |
| The server's disk is short of room | The server does not begin a new upload while its free space, less what it keeps free (`LargeFiles.MinFreeDiskMb`, 10 GiB by default) and what other uploads have reserved, is less than twice the file's size plus 1 MiB. It answers with a time to wait; the collector waits (at most 5 minutes at a time) and asks again, and may send another file meanwhile. Nothing is counted as lost. |
| The server is busy (too many uploads at once, the same file being checked) | The same: it waits the time the server names, then asks again. |
| The access key lost its right to send | The file in hand is refused and counted, said once, and nothing more is uploaded before the next `Start`. |
| The file is larger than the server takes (`LargeFiles.MaxFileSizeBytes`, 64 GiB by default) | Refused, said and counted; the file is left where it is. |
| The server has no file connections (an older server, or file connections switched off) | Said once; asked again with every new session and every 30 minutes. Watched and handed-over files wait. `SendFile` values go the ordinary way. |

On the server an upload that received no piece for 7 days is removed (`LargeFiles.AbandonedUploadDays`); a collector that comes back later begins that file again from the start.

### Secure and ordinary files

**The application decides**, per watch or per hand-over, and the server never changes it:

| | Secure (`Secure = true`, the default) | Ordinary (`Secure = false`) |
|---|---|---|
| Meant for | dumps and anything that holds the memory or data of a process | reports, logs, exports |
| On the server | a zip encrypted with AES-256 under the dump password of the product | the same zip without a password |
| Who can open the download | whoever has the product's dump password | everyone who may see the product |
| A product without a dump password | the file is kept and checked, and cannot be downloaded until a Manager of the product sets the password | packed and downloadable at once |

Values of `SendFile` that take the file connection are always ordinary. The dump password itself is set on the product's settings page; see [Large Files](Large-Files).

### On the server

The value is a value of the File sensor with the file's name and size; its time is the time the server accepted the file, not the file's modification time (which the server keeps as well). A large value has no preview: it is downloaded as its zip. The server keeps it for the sensor's history period like any value.

---

## SendFile above the threshold

On a collector session, a file value of `SendFile`, `SendFileAsync` (C#), `hsm_sensor_add_file`, `hsm_sensor_add_file_from_path`, `FileSensor::SendFile` (native) or the wrapper's `SendFileAsync` **whose content is larger than a quarter of the session's frame limit** goes on the file connection instead of the session, as a large file value that is not secure. With the server's default frame of 4 MiB the threshold is **1 MiB** (1,048,576 bytes). There is no setting for it.

```csharp
IFileSensor reports = collector.CreateFileSensor("reports/daily", new FileSensorOptions { MaxFileSizeBytes = 50L * 1024 * 1024 });

bool taken = await reports.SendFile(@"D:\MyApp\Reports\daily.csv", SensorStatus.Ok, "the daily report");
```

| | Up to the threshold | Above the threshold, on a session |
|---|---|---|
| Path | one element of a `list` frame of the session, as before | the file connection, as a large value of the same File sensor |
| On the server | an ordinary file value with its preview, as before | a large file value: no preview, downloaded as a zip without a password |
| The call | returns as before | returns as before, once the value is taken |
| Size limit of the call | `MaxFileSizeBytes`, 10 MiB by default (at most 128 MiB) in C#; 10 MiB for `hsm_sensor_add_file_from_path` and `FileSensor::SendFile` in the native collector (and the wrapper) | the same |
| Status and comment | kept | kept (a comment is cut to 4,096 characters, and the server keeps 1,024) |

- The content is sent **from memory**: no folder, no state file, nothing deleted, nothing kept across a restart, exactly as for an ordinary value.
- It waits until the sensor's own registration was answered on the session, at most 60 seconds; then, or at once when the server refused that registration, it goes the ordinary way.
- At most 64 MiB of such values wait in memory; one is always taken whatever its size. Past that bound a value goes the ordinary way, said once.
- Routed values go before the files of the large-file API, in the order sent; one that waits goes before a large file in progress after its piece in flight, and that file then continues from the server's piece.
- A value the server refuses for good, or that is given up, is a dropped file value: said with its path, name and size, and counted in `Queue overflow` under `File` (the native collector: in its one overflow counter).
- **Without a file connection** (the HTTP transport, an older server, file connections switched off on the server) the value goes the ordinary way as before: on HTTP files up to the size limit are posted; on a session a file whose value does not fit a frame (about 1 MiB at the default) is dropped, said and counted.
- `Stop`: a value whose upload had not begun goes back before the stop drain (the managed collector: to its file queue; the native collector: to its value queue) and is sent the ordinary way, or dropped and counted when it does not fit a frame; one whose upload had begun is a dropped file value, said and counted.

---

## The native collector (C and C++)

### Building with large files

The functions exist in every build. Uploads need a build with both `HSM_COLLECTOR_HTTP=ON` and `HSM_COLLECTOR_TABLES=ON`: zstd, zlib and libcurl with WebSocket support (8.14.0 or later), and SHA-256 from the system on Windows or from OpenSSL's libcrypto elsewhere (the vcpkg feature `tables` brings it; without vcpkg install `libssl-dev`). In a build without them every watch is `UNAVAILABLE` and every handed-over file ends `UNAVAILABLE`, with the reason.

```bash
cmake -S src/native/collector -B build -DHSM_COLLECTOR_HTTP=ON -DHSM_COLLECTOR_TABLES=ON \
      -DCMAKE_TOOLCHAIN_FILE=<vcpkg>/scripts/buildsystems/vcpkg.cmake
cmake --build build --config Release
```

### Rules that differ from C#

- **Use the session.** Call `hsm_collector_use_session_transport` (C) or `Collector::UseSessionTransport()` (C++) before `Start`. Without it (the in-memory default, or `use_http_transport`) watches become `UNAVAILABLE` with the reason at `Start`. In a build without `HSM_COLLECTOR_HTTP` the call itself fails (C++: throws `hsm::collector::Error`).
- **The outcome of a handed-over file is a handle you poll** (`hsm_large_file_get_result`), not a task; `hsm_large_file_cancel` takes the file out of the queue.
- **Speed limit, C:** 0 in `max_bytes_per_second` is the default (10 MB/s), `HSM_LARGE_FILE_NO_SPEED_LIMIT` (-1) is none. **C++:** `max_bytes_per_second = 0` is none, as in C#.
- **A data folder that cannot be written** does not stop the uploads: it is said once, and files are sent without their state. After a restart the server recognises a file it already has.
- **Identity on Linux.** The native collector identifies a file on Linux by its inode, so the delete compares the open file with the path before it removes the name.
- A file not sent is counted in the one `.module/Collector queue stats/Queue overflow` counter (one counter for all its queues) and said through the logger and `.module/Collector errors`.
- `Stop` waits for the uploader thread to end; on Windows a read blocked on a network share is cancelled.
- **The logger may be called on the uploader thread**: a logger must not call stop, dispose or destroy (a stop or dispose called there does nothing; destroy is not allowed there).
- After dispose the large-file calls on the collector answer `HSM_RESULT_INVALID_STATE`; the calls on a watch or file handle stay valid after the collector is destroyed.

### C

The functions are in `hsm_collector/hsm_collector.h`, section "Large files"; the reference is [`docs/native-collector-c-abi.md`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/docs/native-collector-c-abi.md#large-files-0160).

| Function | Does |
|---|---|
| `hsm_large_file_options_default()` | the options with `struct_size` set and every other field 0, which is every default |
| `hsm_collector_set_data_folder(collector, folder)` | where the state is kept; NULL or `""` restores the default; applies to the watches and files made after the call |
| `hsm_collector_watch_large_files(collector, sensor_path, folder, mask, options, &watch)` | watches a folder; `&watch` may be NULL (the watch then lasts as long as the collector) |
| `hsm_large_file_watch_get_stats(watch, &stats)` | state, files stored, files not sent |
| `hsm_large_file_watch_state_reason(watch, buffer, size)` | why `UNAVAILABLE` or `REFUSED`, or the last problem said |
| `hsm_large_file_watch_release(watch)` | stops watching and frees the handle |
| `hsm_collector_send_large_file(collector, sensor_path, file_path, options, &file)` | hands over one file; returns at once |
| `hsm_large_file_get_result(file, &result)` | the outcome, `source_deleted`, `stored_time_unix_ns` |
| `hsm_large_file_reason(file, buffer, size)` | why the outcome is not `STORED` |
| `hsm_large_file_cancel(file)` | takes the file out of the queue (`CANCELLED`, unless the outcome was settled before or the server's `stored` came first) |
| `hsm_large_file_release(file)` | frees the handle; the file stays queued |

`hsm_large_file_options_t` (every field 0 is its default):

| Field | 0 means | Otherwise |
|---|---|---|
| `struct_size` | (set it to `sizeof`) | |
| `settle_ms` | 60,000 | at least 1,000 |
| `keep_source` | delete the source after the server stored it | 1: keep it, record it as delivered, do not send it again |
| `max_bytes_per_second` | 10,000,000 bytes sent per second | a limit; `HSM_LARGE_FILE_NO_SPEED_LIMIT` (-1): none |
| `no_new_file_alert` | register the sensor with the alert "new data has been received" | 1: without |
| `sensor_location` | Module (`ComputerName/Module/path`) | 1: Product (the path at the root) |
| `description` | none (NULL) | the sensor's description |
| `keep_history_ms` | the server's default | the sensor's history period |
| `ttl_ms` | none | the sensor's TTL |
| `self_destroy_ms` | none | the sensor's self-destroy period |
| `not_secure` | **secure**: a zip under the product's dump password | 1: a zip without a password |

Result codes: `HSM_RESULT_INVALID_ARGUMENT` (with `hsm_collector_last_error`) for a mistake of the call, a full sensor path over 1,024 characters included; `HSM_RESULT_INVALID_STATE` after dispose; `HSM_RESULT_INTERNAL_ERROR` for an unexpected fault inside the library. A collector that cannot send large files does not fail the call. Watch states: `HSM_LARGE_FILE_WATCH_ACTIVE` 0, `UNAVAILABLE` 1, `REFUSED` 2, `STOPPED` 3. Outcomes: `HSM_LARGE_FILE_PENDING` 0, `STORED` 1, `REFUSED` 2, `GIVEN_UP` 3, `MISSING` 4, `UNAVAILABLE` 5, `CANCELLED` 6.

```c
#ifndef _WIN32
#define _POSIX_C_SOURCE 200809L /* nanosleep */
#endif
#include <hsm_collector/hsm_collector.h>

#include <stdio.h>
#include <time.h>
#ifdef _WIN32
#include <windows.h>
static void sleep_ms(int ms) { Sleep((DWORD)ms); }
#else
static void sleep_ms(int ms) { struct timespec t = { ms / 1000, (ms % 1000) * 1000000L }; nanosleep(&t, NULL); }
#endif

/* Watches /var/crash/myapp for *.dmp files and hands over one file, both as values of the File
   sensor "crashes/dumps". */
int main(void)
{
    hsm_collector_options_t options = { 0 };
    options.access_key = "<access key of the product>";
    options.server_address = "https://hsm.example.com";
    options.port = 443;

    hsm_collector_t* collector = NULL;
    if (hsm_collector_create(&options, &collector) != HSM_RESULT_OK)
        return 1;

    /* Large files need the collector session. Call before Start. */
    hsm_collector_use_session_transport(collector);

    /* Optional: where the collector keeps the state of uploads (NULL: the default). */
    hsm_collector_set_data_folder(collector, "/var/lib/myapp/hsm-collector");

    hsm_large_file_options_t file_options = hsm_large_file_options_default(); /* struct_size set, every other field 0: its default */
    file_options.description = "Crash dumps of MyApp";

    hsm_large_file_watch_t* watch = NULL;
    if (hsm_collector_watch_large_files(collector, "crashes/dumps", "/var/crash/myapp", "*.dmp", &file_options, &watch) != HSM_RESULT_OK)
    {
        fprintf(stderr, "watch: %s\n", hsm_collector_last_error(collector));
        hsm_collector_destroy(collector);
        return 1;
    }

    hsm_collector_start(collector);

    /* One file the application knows is complete; kept after the upload. */
    hsm_large_file_options_t keep = hsm_large_file_options_default();
    keep.keep_source = 1;

    hsm_large_file_t* file = NULL;
    if (hsm_collector_send_large_file(collector, "crashes/dumps", "/var/crash/myapp/on-request.dmp", &keep, &file) != HSM_RESULT_OK)
    {
        fprintf(stderr, "send: %s\n", hsm_collector_last_error(collector));
        hsm_large_file_watch_release(watch);
        hsm_collector_destroy(collector);
        return 1;
    }

    hsm_large_file_result_t result = { 0 };
    result.struct_size = sizeof result;
    for (int i = 0; i < 600; i++) /* up to 10 minutes */
    {
        if (hsm_large_file_get_result(file, &result) != HSM_RESULT_OK || result.outcome != HSM_LARGE_FILE_PENDING)
            break;
        sleep_ms(1000);
    }

    char reason[512];
    hsm_large_file_reason(file, reason, sizeof reason);
    printf("outcome %d, source deleted %d %s\n", (int)result.outcome, (int)result.source_deleted, reason);

    hsm_large_file_watch_stats_t stats = { 0 };
    stats.struct_size = sizeof stats;
    hsm_large_file_watch_get_stats(watch, &stats);
    hsm_large_file_watch_state_reason(watch, reason, sizeof reason);
    printf("watch %d: stored %llu, not sent %llu %s\n", (int)stats.state,
        (unsigned long long)stats.files_stored, (unsigned long long)stats.files_not_sent, reason);

    hsm_large_file_release(file);        /* frees the handle; a pending file stays queued */
    hsm_large_file_watch_release(watch); /* stops watching */
    hsm_collector_stop(collector);
    hsm_collector_destroy(collector);
    return 0;
}
```

### C++

[`hsm_collector/large_files.hpp`](https://github.com/SoftFx/Hierarchical-Sensor-Monitoring/blob/master/src/native/collector/include/hsm_collector/large_files.hpp): `Collector::WatchLargeFiles(sensor_path, folder, mask, LargeFileOptions)` returns a move-only `LargeFileWatch` whose destruction stops watching (`State()`, `FilesStored()`, `FilesNotSent()`, `Reason()`); `WatchLargeFilesForLife` watches for the life of the collector without a handle; `SendLargeFile` returns a move-only `LargeFile` whose destruction leaves the file queued (`Outcome()`, `SourceDeleted()`, `StoredTime()`, `Reason()`, `Cancel()`); `SetDataFolder`. They throw `hsm::collector::Error` for a mistake of the call, after dispose, and for an unexpected fault inside the library; never because of the environment. `LargeFileOptions` has `settle` (60 s), `keep_source` (false), `max_bytes_per_second` (10,000,000; 0 = none), `new_file_alert` (true), `location`, `description`, `keep_history`, `ttl`, `self_destroy` and `secure` (true).

```cpp
#include <hsm_collector/hsm_collector.hpp>
#include <hsm_collector/large_files.hpp>

#include <chrono>
#include <iostream>
#include <thread>

using namespace std::chrono_literals;
namespace hc = hsm::collector;

// Watches a folder for *.dmp files, hands over one file and sends a report with SendFile.
int main()
{
    try
    {
        hc::CollectorOptions options;
        options.access_key = "<access key of the product>";
        options.server_address = "https://hsm.example.com";
        options.port = 443;

        hc::Collector collector(options);
        collector.UseSessionTransport(); // large files need the collector session; before Start

        hc::LargeFileOptions dumps_options;
        dumps_options.description = "Crash dumps of MyApp";
        dumps_options.settle = 60s;                     // the default
        dumps_options.max_bytes_per_second = 5'000'000; // 5 MB/s on the wire; 0 = no limit

        hc::LargeFileWatch dumps = collector.WatchLargeFiles("crashes/dumps", "/var/crash/myapp", "*.dmp", dumps_options);

        // A report of a few MB through the ordinary File sensor: above a quarter of the session
        // frame it travels on the file connection by itself.
        hc::FileSensor reports = collector.CreateFileSensor("reports/daily", "daily", "csv");

        collector.Start();

        hc::LargeFileOptions keep;
        keep.keep_source = true;
        hc::LargeFile file = collector.SendLargeFile("crashes/dumps", "/var/crash/myapp/on-request.dmp", keep);

        if (!reports.SendFile("/var/lib/myapp/reports/daily.csv"))
            std::cerr << "the report was not taken (missing, over 10 MiB, or the collector is not running)\n";

        for (int i = 0; i < 600 && file.Outcome() == hc::LargeFileOutcome::Pending; i++)
            std::this_thread::sleep_for(1s);

        std::cout << "file: " << static_cast<int>(file.Outcome()) << ' ' << file.Reason() << '\n'
                  << "watch: " << static_cast<int>(dumps.State()) << ", stored " << dumps.FilesStored()
                  << ", not sent " << dumps.FilesNotSent() << ' ' << dumps.Reason() << '\n';

        // A file value leaves the value queue with the next package (15 s by default); the stop
        // drain sends what is left the ordinary way, so give the report that time before Stop.
        std::this_thread::sleep_for(20s);
        collector.Stop();
        return 0; // dumps and file release their handles here; the watch stops watching
    }
    catch (const hc::Error& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
```

---

## The legacy C++ wrapper (HSMCppWrapper)

Since 2.1.0 the wrapper carries native collector 0.16.0. Its own ABI did not change:

- **`SendFileAsync`** of a file larger than a quarter of the server's frame limit (1 MiB by default) travels on the file connection as a large value of the same File sensor, not secure, when the server has file connections. Up to that size it is sent as before. The wrapper still accepts files up to 10 MiB, and still reads the file on the calling thread.
- **The large-file API** comes through `DataCollectorProxy::Native()`:

```cpp
#include "HSMCppWrapper.h"
#include <hsm_collector/hsm_collector.hpp>   // needed for Native()

#include <string>

// The proxy and the watch live as long as the host: a watch stops when destroyed, and a file value
// leaves the value queue only with the next package (15 s by default).
class Monitoring
{
public:
    explicit Monitoring(const std::string& key)
        : proxy_(key, "https://hsm.example.com", 443, "MyModule")
    {
        // Large files of the native collector underneath, on the same session.
        hsm::collector::LargeFileOptions options;
        options.description = "Crash dumps of MyModule";
        dumps_ = proxy_.Native().WatchLargeFiles("crashes/dumps", "D:\\MyApp\\Dumps", "*.dmp", options);

        proxy_.Start();
    }

    // A file of a few MB: above a quarter of the session frame it travels on the file connection.
    void SendReport() { proxy_.SendFileAsync("reports/daily", "D:\\MyApp\\Reports\\daily.csv"); }

    ~Monitoring() { proxy_.Stop(); }

private:
    hsm_wrapper::DataCollectorProxy proxy_;
    hsm::collector::LargeFileWatch dumps_;
};
```

- The wrapper uses the collector session by default. After `proxy.UseHttpTransport()` large files are `Unavailable` and `SendFileAsync` posts files up to 10 MiB over HTTP, as before.
- Against a server without file connections the behaviour of 2.0.0 stays: a file that does not fit a session frame is dropped, logged (`Dropped a value of N bytes: it is larger than a collector session frame (M bytes).`) and counted in `Queue overflow`.

---

## The transport and the server

| Collector on | Against | What you see |
|---|---|---|
| the session | a server with large file values (3.46.0 or later) | watches `Active`, files stored; `SendFile` above the threshold as a large value |
| HTTP (`CollectorTransport.Http`, `UseHttpTransport()`) | any server | watches `Unavailable`, handed-over files `Unavailable`; `SendFile` posts files as before |
| the session | a server with sessions but without large file values (the releases 3.43.0 to 3.45.0) | the collector says once that the server does not take large files and asks again every 30 minutes; files wait; `SendFile` values go the ordinary way (a file above about 1 MiB does not fit a frame and is dropped, said and counted) |
| the session | a server with file connections switched off (`CollectorSessions.MaxFileFrameBytes = 0`) | the same as the row above |
| the session | a server without sessions (before 3.43.0) | no session comes up: the collector sends nothing at all, values included |

What the server allows is announced when the file connection opens and logged: `The file connection is up: frame limit 4194304 bytes, piece 4173824 bytes, file limit 68719476736 bytes.` The server's settings are on [Server Configuration](Server-Configuration#large-files).

---

## When something goes wrong

| Symptom | Look at | Likely cause |
|---|---|---|
| A watch is `Unavailable` | `StateReason` | the HTTP transport, a custom `DataSender`, .NET Framework with an untrusted certificate, a data folder the account cannot write; native: a build without large files, a libcurl without WebSocket, or a transport other than the session |
| A watch is `Refused` | `StateReason` | the server refused the File sensor's registration (another kind of sensor on that path, a path it does not accept) |
| A file stays in the folder, nothing in the log | the settle time; whether a program still writes it | the file still changes, or another program holds it open for writing (Windows) |
| "The server does not take large files (…)" | the server's version and `CollectorSessions.MaxFileFrameBytes` | an older server, or file connections switched off |
| "The server cannot take the large file … now (…); trying again in N s." | the server's free space and `LargeFiles` settings | the server's disk is short of room, or it runs its most uploads at once |
| A file is `Refused` | `Reason`, `Collector errors` | larger than the server takes, or the key may not send to that sensor |
| `Queue overflow` shows `Large files: N` | `Collector errors` | the reason per file, above |
| The file is stored, the download is refused | [Large Files](Large-Files) | a secure file of a product without a dump password yet |

---

## Limits and known limits

- **One file at a time** per collector, on one background thread.
- **Symbolic links and reparse points** in a watched folder are skipped (said and counted once per path and run): a link may point outside the folder, and the collector never deletes through one. Hand over the target itself if you need it.
- **A file whose writer died** in the middle stops changing like a complete one: after the settle time it is sent as it is. An application that knows when a file is complete hands it over with `SendLargeFileAsync` / `hsm_collector_send_large_file` instead of relying on the settle time.
- **Systems without file identity.** On Windows the collector knows a file by its file id and holds it open against writers and deleters for the whole upload. Off Windows the managed collector identifies a file by its name, size and modification time (the native collector on Linux adds the inode), and checks them again before the end of the upload and before the delete; a writer that keeps writing is caught by the settle time and by those checks.
- **A hard link** loses one name when its source is deleted.
- **Names.** A file name of more than 1,024 characters, or (native, Linux) one that is not valid UTF-8, cannot be sent: the file is refused and counted.
- **Network shares.** On Linux the native collector's `Stop` returns once a read in progress completes; on Windows it cancels the read. The managed collector waits for the uploader at most a second.
- **Handed-over files are kept in memory**: after a restart of the process the application hands them over again.

---

## Try it

From an empty server to a dump in a watched folder, its download and the opened archive, and the same for a file of a few megabytes sent with `SendFile`. You need Docker Desktop, the .NET 8 SDK, `curl`, 7-Zip and a clone of the repository.

**1. Run a server behind the bundled Caddy.** The download goes through Caddy, so run both. From the repository root (branch `master`, 3.46.0 or later), in PowerShell, build the server and the Caddy image under names of their own:

```powershell
dotnet publish src/server/HSMServer/HSMServer.csproj -c Release --os linux --arch x64 -o ..\hsm-publish
docker build -t hsm-largefiles:local -f docker_scripts/HSMserver/Dockerfile.local ..\hsm-publish
docker build -t hsm-largefiles:local --build-arg BASE_IMAGE=hsm-largefiles:local -f docker_scripts/HSMserver/Dockerfile.healthcheck docker_scripts/HSMserver
docker build -t hsm-largefiles-caddy:local caddy
```

Then a compose override `..\compose.largefiles.yml` that gives the stack its images, names and a port of its own (`!override` replaces the ports of the reference file; Docker Compose 2.24 or later):

```yaml
services:
  app:
    image: 'hsm-largefiles:local'
    container_name: hsm-largefiles-app
  caddy:
    image: 'hsm-largefiles-caddy:local'
    container_name: hsm-largefiles-caddy
    ports: !override
      - '127.0.0.1:18443:443'
```

```powershell
$env:HSM_DOMAIN = 'localhost'; $env:HSM_CERTIFICATE = 'self-signed'
docker compose -p hsm-largefiles -f docker-compose.yml -f ..\compose.largefiles.yml up -d
curl.exe -k -o NUL -w "%{http_code}`n" https://localhost:18443/api/sensors/testConnection
```

Expect `Container hsm-largefiles-app Healthy`, `Container hsm-largefiles-caddy Started`, and `200`. The data folders (`Databases`, `LargeFiles`, ...) appear in the repository root. Below, `https://localhost` stands for `https://localhost:18443`.

**Create a product.** Open the site and sign in (see [Installation](Installation) for the first sign-in). On the products page click **Add product**, name it `LargeFileDocs`, click **Add**. On the access keys page the row of `LargeFileDocs` shows `DefaultKey` with the permission **Full**: copy the key.

**2. Make the program.** Create a folder with the project file of [Table Sensors: A complete program](DataCollector-Table-Sensors#a-complete-program) (set `<repository>` to your clone) and this `Program.cs`. `dotnet build -c Release`. Expect "Build succeeded" (warnings `CA1416` of the collector project are expected).

```csharp
using System;
using System.IO;
using System.Threading.Tasks;
using HSMDataCollector.Core;
using HSMDataCollector.Logging;
using HSMDataCollector.Options;
using HSMDataCollector.PublicInterface;

// Watches the folder "dumps" for *.dmp files (the File sensor "crashes/dumps") and sends a file of 3 MB
// with SendFile (the File sensor "reports/daily"). Set HSM_KEY to an access key of the product;
// HSM_SERVER and HSM_PORT default to https://localhost and 443.
internal static class Program
{
    private static async Task<int> Main()
    {
        var key = Environment.GetEnvironmentVariable("HSM_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Console.Error.WriteLine("Set HSM_KEY to an access key of the product.");
            return 2;
        }

        var collector = new DataCollector(new CollectorOptions
        {
            ServerAddress = Environment.GetEnvironmentVariable("HSM_SERVER") ?? "https://localhost",
            Port = int.Parse(Environment.GetEnvironmentVariable("HSM_PORT") ?? "443"),
            AccessKey = key,
            ClientName = "large-files-try-it",
            AllowUntrustedServerCertificate = true, // a test server's self-signed certificate
        });
        collector.AddCustomLogger(new ConsoleLogger());

        var folder = Path.GetFullPath("dumps");
        Directory.CreateDirectory(folder);

        using var watch = collector.WatchLargeFiles("crashes/dumps", folder, "*.dmp", new LargeFileOptions
        {
            SettleTime = TimeSpan.FromSeconds(10), // 60 s by default; short for the walk-through
            SensorOptions = new FileSensorOptions
            {
                Description = "Crash dumps of the try-it program",
                SensorLocation = SensorLocation.Product, // the path starts at the product root
            },
        });
        Console.WriteLine($"Watching {watch.Folder} for {watch.Mask}: {watch.State} {watch.StateReason}");

        var report = collector.CreateFileSensor("reports/daily", new FileSensorOptions { SensorLocation = SensorLocation.Product });

        await collector.Start();

        // An ordinary file value of 3 MB: more than a quarter of the session frame (1 MiB by default),
        // so on a session it travels on the file connection as a large file value that is not secure.
        var reportPath = Path.GetFullPath("report.csv");
        File.WriteAllLines(reportPath, MakeReport(60_000));
        Console.WriteLine($"SendFile {new FileInfo(reportPath).Length} bytes: {await report.SendFile(reportPath)}");

        Console.WriteLine($"Copy a file named *.dmp into {folder}; waiting up to 10 minutes for it.");
        for (var i = 0; i < 600 && watch.FilesStored + watch.FilesNotSent == 0; i++)
            await Task.Delay(1000);

        Console.WriteLine($"Watch: {watch.State}, stored {watch.FilesStored}, not sent {watch.FilesNotSent} {watch.StateReason}");

        await collector.Stop();
        collector.Dispose();
        return watch.FilesStored > 0 ? 0 : 1;
    }

    private static string[] MakeReport(int lines)
    {
        var rows = new string[lines];
        for (var i = 0; i < lines; i++)
            rows[i] = $"{i},{DateTime.UtcNow.AddSeconds(-i):O},{Math.Sin(i / 100.0):F6}";
        return rows;
    }

    private sealed class ConsoleLogger : ICollectorLogger
    {
        public void Debug(string message) { }
        public void Info(string message) => Console.WriteLine($"info: {message}");
        public void Error(string message) => Console.WriteLine($"error: {message}");
        public void Error(Exception ex) => Console.WriteLine($"error: {ex.Message}");
    }
}
```

**3. Run it.**

```powershell
$env:HSM_KEY = "<the key>"
$env:HSM_SERVER = "https://localhost"; $env:HSM_PORT = "18443"   # the port of step 1
dotnet run -c Release --no-build
```

Expect, within a few seconds (this run used port 18443; the paths are yours):

```
Watching D:\try-it\dumps for *.dmp: Active
info: New sensor has been added reports/daily
info: DataCollector (v. 4.2.0.0) -> Starting
info: The collector session is enabled and the Service alive sensor (AddCollectorAlive) is not registered at Start: without it the server has no disconnect alert for this collector.
info: DataCollector (v. 4.2.0.0) -> Running
info: File: D:\try-it\report.csv has been send
SendFile 2718735 bytes: True
Copy a file named *.dmp into D:\try-it\dumps; waiting up to 10 minutes for it.
info: The collector session is up: wss://localhost:18443/api/sensors/session, frame limit 4194304 bytes, heartbeat 30 s.
info: The file connection is up: frame limit 4194304 bytes, piece 4173824 bytes, file limit 68719476736 bytes.
info: The file value of reports/daily (2718735 bytes) is stored on the server as a large file value.
info: The file connection ended: no file waits.
```

The `SendFile` of 2.7 MB went on the file connection by itself, because it is larger than 1 MiB. Failures:

| Output | Meaning |
|---|---|
| `Watching … : Unavailable large files need the collector session transport, …` | the collector sends over HTTP; the text says why |
| no "collector session is up" line | the server is not reachable at that address and port, or the key is wrong |
| no "file connection is up" line, and `error: The server does not take large files (…): large files wait in their folders and are offered again every 30 minutes and with every new session.` | the server has no large file values, or file connections are switched off; the 2.7 MB `SendFile` value then does not fit a session frame and is dropped and counted |

**4. Put a dump into the folder.** Any file whose name ends in `.dmp` will do. Copy a file of some tens of megabytes next to the program under that name, note its hash, and move it in (a move makes it appear whole):

```powershell
Copy-Item <any file of some tens of MB> .\app-crash.dmp
Get-FileHash .\app-crash.dmp
Move-Item .\app-crash.dmp .\dumps\
```

Our run used a file of 40,000,000 bytes. About 10 seconds later (the settle time of the program; 60 s by default), expect:

```
info: The file connection is up: frame limit 4194304 bytes, piece 4173824 bytes, file limit 68719476736 bytes.
info: The large file D:\try-it\dumps\app-crash.dmp is stored on the server and deleted here.
info: The file connection ended: no file waits.
Watch: Active, stored 1, not sent 0
info: DataCollector (v. 4.2.0.0) -> Stopping
info: DataCollector (v. 4.2.0.0) -> Stopped
```

The file is gone from `dumps`, and the program exits with code 0. A file that stays in the folder is still being written, or is held open by another program.


**5. Look at the values.** On the site the tree of `LargeFileDocs` now has `crashes/dumps` and `reports/daily`, both File sensors. On the **Files** tab of `reports/daily`: `report.csv`, `not secure`, `ready`, with **Download**. On `crashes/dumps`: `app-crash.dmp`, `secure`, `waiting for the product's password`, under the notice "Secure large files of this product are kept and cannot be downloaded: the product has no dump password yet. …". Had you opened the tab during the upload, it would have shown `uploading, N%`, then `being checked`. The sensor's settings show its alert "New data has been received" with the message `[LargeFileDocs]crashes/dumps new file app-crash.dmp (38.15 MB) https://localhost/Home/<sensor id> waits for the product's dump password`. The page is described on [Large Files](Large-Files).

The same over the REST API, with an API token of yours (your profile page, section **API tokens**; read-only is enough), as `TOKEN` below, and the product's id (in the address of its settings page):

```bash
curl -k -H "Authorization: Bearer TOKEN" "https://localhost/api/v1/sensors?product=<product id>"
```

Each File sensor's `lastValue.value` has the name without the extension and the extension apart (`"name": "app-crash", "extension": "dmp"`), the size, and a `largeFile` block; the value's `time` is the time the server accepted the file. For `report.csv` (not secure) the archive is ready at once:

```json
"largeFile": { "fileId": "…", "secure": false, "state": "packed", "notReadyReason": null,
               "archive": { "id": "…", "size": 799749, "sha256": "…", "passwordVersion": null,
                            "downloadUrl": "https://localhost/api/v1/large-files/…/archives/…" } }
```

For `app-crash.dmp` (secure) on a product without a dump password:

```json
"largeFile": { "fileId": "…", "secure": true, "state": "verified",
               "notReadyReason": "The file is kept but cannot be downloaded: its product has no dump password yet. A Manager of the product sets it in the product settings.",
               "archive": null }
```

**6. Set the dump password.** Open the product's settings page: the section **Dump password** says "Not set". Click **Generate**, copy the password, click **Save**: "Saved: dump password version 1. The kept secure archives of the product are re-encrypted with it in the background." Within a minute the dump's row turns `being packed`, then `ready`; over the API `GET /api/v1/sensors/<sensor id>/large-files/<file id>` answers `"state": "packed"`, `"passwordVersion": 1` and a `downloadUrl`.

**7. Download both.** Click **Download** in each row, or with the token:

```bash
curl -k -OJ -H "Authorization: Bearer TOKEN" "<downloadUrl of report.csv>"
curl -k -OJ -D - -H "Authorization: Bearer TOKEN" "<downloadUrl of app-crash.dmp>"
```

Expect `report.csv.zip` and `app-crash.dmp.zip`. The headers of the second show `Server: Caddy` (the bundled Caddy gives out the bytes once HSM has checked the request), `Content-Disposition: attachment; filename=app-crash.dmp.zip`, `Repr-Digest: sha-256=:…:` and `X-Hsm-Password-Version: 1`; the first has no `X-Hsm-Password-Version`. Failures: 404 (a wrong id, a product you may not see, or a link to a zip replaced by a new password), 409 with the reason (not packed yet: wait), 401 (no token; an access key never downloads), 503 (the download cannot be recorded now: try again later).

**8. Open the archives.** `report.csv.zip` opens without a password. `app-crash.dmp.zip` asks for one: give the dump password. In 7-Zip:

```
7z l -slt app-crash.dmp.zip     → Path = app-crash.dmp, Size = 40000000, Encrypted = +, Method = AES-256 Deflate
7z t -p<the password> app-crash.dmp.zip     → Everything is Ok
7z x -p<the password> app-crash.dmp.zip
```

The extracted `app-crash.dmp` has the hash `Get-FileHash` printed in step 4. A wrong password gives "Wrong password". The archiver built into Windows usually cannot open an AES zip; use 7-Zip or WinRAR.

**9. See a failure on purpose.** Add `Transport = CollectorTransport.Http` to the collector's options and run again: the log says `Large files cannot be sent: large files need the collector session transport, and this collector sends over HTTP (CollectorOptions.Transport).`, the program prints `Watching … : Unavailable …`, and a dump put into the folder stays there. The `SendFile` of 2.7 MB is posted over HTTP as an ordinary file value: its description has `"largeFile": null`, and the **Files** tab shows it with **Preview**.

**10. Clean up.** `docker compose -p hsm-largefiles -f docker-compose.yml -f ..\compose.largefiles.yml down -v` at the repository root, with the two variables of step 1 set. The data stays in the folders of the repository root; delete them to start empty next time.
