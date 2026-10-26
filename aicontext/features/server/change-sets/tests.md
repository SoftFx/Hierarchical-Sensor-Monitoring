# Tests: Change Sets (undo journal with rollback)

> Owner: server | Last reviewed: 2026-10-19 | Canonical: yes

The primary seam is the service layer on the real cache and the real test LevelDB (`MonitoringCoreTestsBase` fixtures), exactly like the policy-administration suites; transport layers get contract tests; the web UI gets Playwright.

## Suites

- `src/tests/HSMServer.Core.Tests/ChangeSets/ChangeSetJournalTests.cs` — the persistence facade on the real ChangeSets table: append/group/sequence, author fixation by the first write, status transitions (`MarkRolledBack`, `MarkEntryRestored`), durability across journal recreation (simulated restart), and `PurgeReferences` (entry drop → `partial`, entry-less removal, fan-out image drop, folder-bound template purge).
- `src/tests/HSMServer.Core.Tests/Model/ManagementApi/ChangeSetJournalingTests.cs` — journaling at the service seam: before/after images with the changeSetId echo, grouping under one client id, standalone change sets for ungrouped writes, failed writes journal nothing, dry-run applies nothing, template create/delete carry per-sensor fan-out images, schedule rows journal.
- `src/tests/HSMServer.Core.Tests/Model/ManagementApi/ChangeSetRollbackTests.cs` — rollback semantics: clean rollback, content-conflict refusal with the per-entity diff, no false positive on a web-editor-style identical re-assert, force override, dry-run plan, redo via rollback of the rollback (and terminality of the original), template rollback restoring the row plus every sensor list, template-delete rollback re-creating under the same id, schedule-delete rollback restoring references, list/get filtering, 404 for unknown ids, deletion cleanup (entry-less removal, partial + survivor rollback).
- `src/tests/HSMServer.Core.Tests/Controllers/ChangeSetsApiControllerTests.cs` — REST contract: uniform 404/409 rendering with details, the rollback header echo (absent on dry-run), filter pass-through, the management-area metadata pins.
- `src/tests/HSMServer.Core.Tests/Controllers/SensorPoliciesApiControllerTests.cs` — the write-transport headers: `X-HSM-Change-Set` echoed on 201, `X-HSM-Dry-Run` answering the preview without applying.
- `src/tests/HSMServer.Core.Tests/Mcp/ChangeSetsMcpToolsTests.cs` — tool delegation with the ambient principal, the paging envelope, uniform tool-error rendering.
- `HsmMcpServerRegistrationTests` / `HsmMcpWireTests` — the three new tools in the spec-tool pins; the relaxed output-schema pin (union write tools are permissively `true`); the zero-match warning documentation riding the tool description.
- `src/tests/Autotests/change_sets/change_sets.spec.ts` — Playwright: menu link, page render, table shell. The full rollback-dialog flow needs a journaled write, which the cookie session cannot produce (web-editor writes are not journaled and the management API authenticates tokens) — that flow is covered at the service seam; a token-provisioning autotest fixture is the follow-up.

## Conventions to keep

- New journal behavior gets a service-seam test first (apply → journal → roll back → observe state); internal journal internals are never asserted from transport tests.
- Any new journaled write endpoint updates: the header/argument carriers, the echo assertions, and (MCP) the wire/registration tool pins.
- Conflict-rule changes must keep the "web-editor re-assert is not a conflict" test green.
