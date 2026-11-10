# HSM Server

## Management API
* Sensor deletion: `DELETE /api/v1/sensors/{id}` removes a sensor irreversibly — cache entry, attached policies and mute state, the whole stored history and undo references — through the same engine the web UI's remove-node uses; `X-HSM-Dry-Run: true` answers 200 with a preview (identity, product, state, affected policies, history size and last-value time) and applies nothing; a removal that never reached the cache answers 409 instead of a ghost success (#1637).
* Bar sensor values now carry `StdDev` in `GET /api/v1/sensors/{id}/history` and in the sensor's `lastValue` — normalized like every other surface: unknown, negative and non-finite σ render as `null`, 0 stays 0 (#1620).
* Policy writes that carry an effectful destination but no notification are rejected with a field-keyed 422 instead of being silently stored as an empty destination — a caller can no longer believe alerts route to a chat while nothing is ever sent (#1612).

## MCP
* New `delete_sensor` tool (33 tools now) with a `dryRun` parameter mirroring the REST preview/value envelope and error parity (#1637).
* `get_sensor_history` and `get_sensor` bar points carry `StdDev` (#1620).
* A client aborting its request before the response finishes (any endpoint, the noisy case being a polling MCP client) is logged at Warn, not ERROR — client disconnects no longer flood `HSM-error-log.txt` (one 3.41.9 install saw ~300 entries per 24h); internal cancellations and real errors stay at ERROR (#1632).

## Alerts — templates
* Template `isDisabled` edits now propagate to the sensor policies the template already applied; a deliberate per-sensor toggle still wins and is never overwritten (#1606).
* After a per-sensor toggle the template is applied again on its next save: new conditions, message and interval land, the operator's disable choice survives — previously the template never re-applied to a toggled policy and the drift went unreported. The preservation is reported (a save warning + journal record) only when the kept choice carries new information; a collector re-registration can no longer release the operator's TTL toggle (#1077).
* A throwing policy evaluator (e.g. a `Length` condition over a stored null string) no longer breaks the policy update or diverges memory from the database: the failing policy is skipped for the evaluation pass, marked with an error naming the policy id, and its siblings still evaluate; the update and the DB write proceed (#1563).
* Write-time validation, REST and MCP alike: a path pattern of literals without `/` (provably dead — it can never match) answers 400 (#1608); curly braces in a policy message answer 400 per the `string.Format` contract (#1608); a TTL policy carrying conditions answers a clean field-keyed 400 instead of a leaked `NotImplementedException` (#1608).
* Condition rejections are field-keyed validation errors naming the exact policy, condition and field (`policies[0].conditions[0].property: ...`) with the same message catalogue the per-sensor surface uses — no more raw .NET exception text under one bare `policies` prefix, and AnyType errors name AnyType, never the internal stand-in (#1611).
* Malformed path regexes and out-of-range enum values are rejected with field names before binding fails, and a body with several mistakes reports every field instead of one generic error (#1607).
* Web UI: server-side path validation errors (the mixed-type template check, invalid patterns, empty list) render inline over the preserved input on a blocked Save — the form no longer silently re-renders unchanged (#1247).

## Alerts — schedules
* YAML validation 422s are deterministic and field-specific: no raw .NET exception text, no server-generated GUIDs in messages; empty YAML, bad times, unknown days, malformed dates and reversed/overlapping windows each name their exact defect (#1599).
* A schedule YAML missing the required `daySchedules` key is rejected on the write path (a typo could silently store a "never working" schedule); schedules stored before this rule keep loading (#1598).
* The web UI name limit aligns with the API's 200-char contract (input `maxlength`, server-side check), and long names truncate with a tooltip instead of stretching the schedule table (#1597).

## Journal
* Alert template writes produce visible, rollbackable `alertTemplate` journal records — previously they were invisible to every caller including admins; the per-sensor fan-out records carry the write's undo-group link and the true author (#1610).
* Global and folder-scoped records (alert schedules, alert templates) stay visible when a product filter is selected — selecting a product can no longer make the journal page look empty while it holds records (#1601).

## Infrastructure
* The ASP.NET Core Data Protection key ring (auth cookies, antiforgery tokens) persists in the mounted `Config/DataProtection-Keys/` — cookie sessions and form submits survive a container recreate (image upgrade, compose change). Existing installs need no compose change; the update **to** this version performs one final forced re-login. Do not delete `Config/DataProtection-Keys/` afterwards (#1633).
* Chrome no longer logs a Permissions-policy violation on tree pages (deprecated `unload` handler removed) (#1579).
* A stale merge-conflict artifact (`_AlertSchedule.cshtml.orig`) no longer ships in clones; `*.orig`/`*.rej`/`*.bak` are git-ignored (#1640).

## Dependencies
* Bundled HSMDataCollector 3.5.0, HsmAgent 0.5.28 and the Linux probe 0.10.0 — all unchanged since 3.41.9.
