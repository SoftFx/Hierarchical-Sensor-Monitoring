# HSM Server

## Management API & MCP
* Products and their access keys (create, issue, list, edit, revoke) are managed via the management API and MCP (#1786, #1787, #1789).
* The HSM agent installer downloads through one-time links (#1788).

## Sign-in
* Optional SSO sign-in via OpenID Connect (#1782), off by default (`Oidc` section); password login stays. SSO admin only for passwordless users; user administration is admin-only.

## History storage
* Closed weeks of sensor history can be sealed and compacted in background jobs — both off by default (`HistorySealing`, `HistoryCompaction`; compaction in daily UTC windows) (#1801–#1805); aggregated runs end at the storage-week boundary (#1800); an unreadable sealed chunk surfaces on reads (#1803).
* LevelDB 1.2.2; Linux writes stay uncompressed.

## Telegram
* The bot token never appears in the server logs (#1824).

## Notifications
* An aggregated chat no longer goes silent after an alert with an empty variable; alerts arriving mid-build are not lost (#1776, #1778).
* Per-alert trace lines moved from Info to Debug: Release logs carry two lines per alert, not seven.

## Validation & API
* Policy templates with `{`/`}` and control/bidi characters in names and templates are rejected; alert texts are sanitized (#1674, #1705); credential-bearing URLs in string values are masked on ingest (#1709).
* TTLs have a 1-minute floor, self-triggering status policies are rejected (#1673, #1707); schedule YAML: no anchors/aliases, bounded size (#1706).
* A malformed `/list` element or dry-run header answers 400, an unknown template type returns only the `sensorType` error (#1672, #1675, #1677); `testConnection` without credentials answers 406 (#1678); a read-only token's write denial says so (#1708); product policy endpoints are authorized through the root product (#1680).

## Web UI
* Journal fan-outs are paged, legacy-row filters work (#1688, #1679); chat and folder dates show in UTC (#1676).
