# HSM Server

## Restore
* New Alert Template restore wizard: import alert templates from a backup file into the current server, with existing templates detected and marked, and camelCase field mapping preserved.

## Bug fixes
* Fixed silent wipe of alert templates on server restart caused by erroneous deduplication compaction; the misleading compaction hint was also removed.
* Backup restore now creates the target directory before opening the backup database, preventing a failure on fresh paths.
