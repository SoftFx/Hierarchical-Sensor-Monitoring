# HSM Server

## Notifications
* A chat with aggregated messages no longer goes silent until a restart after an alert with an empty variable (`$comment`, `$unit`...); an alert that cannot be grouped is sent as it is (#1776).
* Alerts arriving while a chat's aggregated message is built are no longer lost (#1778).
* Five per-alert trace lines moved from Info to Debug: Release logs write two lines per alert instead of seven.

## Stricter validation
* Sensor-policy templates with `{` or `}` are rejected (#1674); control and bidi-override characters are rejected in names and templates, alert texts are sanitized (#1705).
* TTL and template TTL: at least 1 minute, bounded to the DateTime range; status policies that would trigger themselves are rejected (#1673, #1707).
* Schedule YAML: no anchors or aliases, bounded size and dates (#1706).

## API
* Version sensor values are accepted on the Sensor API (#1671).
* A malformed `/list` element or `X-HSM-Dry-Run` header answers 400 instead of 500 (#1672, #1675); an unknown template type returns only the `sensorType` error (#1677).
* `testConnection` without credentials answers 406 (#1678); a read-only token's write denial says the token is read-only (#1708).
* Product policy endpoints are authorized through the node's root product (#1680).

## Web UI
* Journal: large record fan-outs are paged (#1688); filters work for legacy rows (#1679).
* Chat and folder dates are shown in UTC (#1676).
