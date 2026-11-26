# HSM DataCollector 4.1.0-preview

Prerelease for testing. New since 4.0.0-preview: table sensors.

## New
* **Table sensors** (`CreateTableSensor(path, TableSensorOptions)`, ADR-0021): rows of a time and nullable `long`/`double` columns, added from any thread with `AddRow`. The rows of all table sensors are packed and compressed every `CollectorOptions.TableTickPeriod` (10 s) and sent on a second WebSocket of the collector session, with acknowledgements; a block is sent again after a lost connection, never stored twice.
* Every row that cannot be delivered is counted per sensor (`ITableSensor.RowsDropped`, `State`, `StateReason`) and reported in the collector's own sensors; nothing throws because the network or the server is missing.
* Needs **HSMServer 3.45.0** or later and the session transport. On `CollectorOptions.Transport = Http` a table sensor is `Unavailable` and its rows are counted as dropped.

## Package
* Bundles `HSMTableBlocks.dll` (the block format); new dependencies `ZstdSharp.Port` 0.8.8 (managed zstd, no native binaries) and `System.IO.Hashing` 8.0.0.

Docs: wiki page "DataCollector Table Sensors".
