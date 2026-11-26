# HSM Server

## Read before upgrading
* "Keep sensor history", "Remove sensor after inactivity" and TTL set on a folder to 1, 3 or 6 months or 1 year now apply to the products that inherit them (before, they were never applied): on the first cleanup after the upgrade those products lose history older than the folder's period, and sensors idle for longer are removed. Each affected product is logged once (#1864).
* The Sensor API's CSV export (`historyFile`) now doubles a quote inside a value (`"a""b"`); 3.45 wrote the value as it was. Correct CSV, but a change for a script that parsed the old output.
* Take the new `docker-compose.yml`: Caddy image `2.11.4-9`, the `LargeFiles` folder (`HSM_LARGE_FILES`), `HSM_DOMAIN` passed to the server.

## Large file values (new, #1882, ADR-0022)
* Collectors upload large files, crash dumps above all, of up to 64 GiB in compressed pieces over the collector session (managed collector 4.2.0-preview, native collector 0.16.0). They are values of a File sensor, kept on the server's disk.
* The sensor's Files tab shows each file with its state; an alert on a new file; a file downloads as a zip.
* A dump is a secure file: an AES-256 zip under the product's dump password, set by a Manager of the product. Downloads are checked and logged by HSM and given out by the bundled Caddy.
* REST API and MCP access with an API token.
* A file value larger than a quarter of the session frame (1 MiB by default) sent on a session is stored as a large file value: the server's acceptance time, no preview, downloaded as an archive.
* Settings in the `LargeFiles` and `LargeFileArchives` sections (all with defaults). Docs: wiki pages "Large Files" and "DataCollector Large Files".

## Shutdown
* A history week is closed only after its running reads and writes (#1958).
