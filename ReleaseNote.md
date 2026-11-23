# HSM Server

## Management API & MCP
* Products can be created and their access keys issued, listed, edited and revoked through the management API and MCP (#1786, #1787, #1789).
* The server serves the HSM agent installer through one-time download links (#1788).

## Sign-in
* Optional SSO sign-in for the web UI via OpenID Connect (#1782), off by default (`Oidc` config section); password login stays. SSO admin is granted only to users without a password; user administration is admin-only.

## History storage
* Closed weeks of sensor history are sealed and compacted in background jobs (#1801–#1804; `HistoryCompaction` is off by default, with daily UTC working hours); aggregated runs end at the storage-week boundary (#1800); an unreadable sealed chunk surfaces on point reads (#1803).
* SoftFX.LevelDB.Standard 1.2.2; Linux writes stay uncompressed.

## Telegram
* The bot token never appears in the server logs (#1824).

## Notifications
* An aggregated chat no longer goes silent after an alert with an empty variable (#1776); alerts arriving while the aggregated message is built are not lost (#1778).

## Validation & API
* Policy templates with `{`/`}` are rejected; control and bidi characters are rejected in names and templates, alert texts are sanitized (#1674, #1705); credential-bearing URLs in string sensor values are masked at ingestion (#1709).
* TTLs are bounded with a 1-minute floor, self-triggering status policies are rejected (#1673, #1707); schedule YAML rejects anchors/aliases and is size-bounded (#1706).
* A malformed `/list` element or dry-run header answers 400, an unknown template type returns only the `sensorType` error (#1672, #1675, #1677); `testConnection` without credentials answers 406 (#1678); a read-only token's write denial says so (#1708); version sensor values are accepted (#1671).

## Web UI
* Journal record fan-outs are paged (#1688); chat and folder dates are shown in UTC (#1676).
