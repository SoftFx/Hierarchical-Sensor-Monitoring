# HSM DataCollector 4.2.0-preview

Prerelease for testing. New since 4.1.0-preview: large files.

## New
* **Large files** (`ILargeFileCollector`, ADR-0022): `WatchLargeFiles(sensorPath, folder, mask, LargeFileOptions)` uploads every settled file of a watched folder (crash dumps above all); `SendLargeFileAsync(sensorPath, filePath, LargeFileOptions)` hands over one file and completes with its outcome. A file is uploaded in compressed pieces on a third WebSocket of the collector session, at most 10 MB/s by default, continues from the server's piece after a lost connection or a restart, and its source is deleted once the server has stored and checked it. On the server it is a value of a File sensor: secure by default (a zip under the product's dump password).
* `LargeFileOptions`: `SettleTime` (60 s), `DeleteAfterUpload`, `MaxBytesPerSecond`, `NewFileAlert`, `Secure`, `SensorOptions`. `CollectorOptions.DataFolder` keeps the upload state across restarts (the user's local application data by default).
* Watches and handed-over files that cannot be sent are reported and counted; nothing throws because the network or the server is missing.

## Behaviour changes
* On a session, a file value larger than a quarter of the session frame (1 MiB by default) goes on the file connection and is stored as a large file value that is not secure; before, it was dropped and counted.
* `Stop`: such a value whose upload has not begun goes in the stop drain the ordinary way (#1965); one whose upload has begun is dropped, said and counted.

## Compatibility
* Large files need **HSMServer 3.46.0** or later and the session transport. Against an older server, or on `CollectorOptions.Transport = Http`, file values behave as before and watched or handed-over files are not sent.

Docs: wiki page "DataCollector Large Files".
