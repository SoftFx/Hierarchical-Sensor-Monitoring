# Tests: Journal (unified configuration journal with rollback)

> Owner: server | Last reviewed: 2026-10-27 | Canonical: yes

The primary seam is the service layer on the real cache and the real test LevelDB (`MonitoringCoreTestsBase` fixtures), exactly like the policy-administration suites; transport layers get contract tests; the web UI gets Playwright.

## Suites

- `src/tests/HSMServer.Core.Tests/ChangeSets/ChangeSetJournalTests.cs` — the internal persistence facade on the real ChangeSets table: append/group/sequence, status transitions (`MarkRolledBack`, `MarkEntryRestored`), durability across journal recreation, `PurgeReferences`. Since #1588 an append always lands in the given id (the fork rule is gone with client ids).
- `src/tests/HSMServer.Core.Tests/Model/ManagementApi/ChangeSetJournalingTests.cs` — journaling at the service seam: before/after images under the request group, two writes under one group, single-entry groups for separate requests, failed writes journal nothing, dry-run applies nothing, template create/delete carry per-sensor fan-out images, schedule rows journal.
- `src/tests/HSMServer.Core.Tests/Model/ManagementApi/ChangeSetRollbackTests.cs` — the internal rollback semantics: clean rollback, content-conflict refusal with the per-entity diff, no false positive on an identical re-assert, force override, dry-run plan, redo via rollback of the rollback, template/schedule rollback restores, foreign principals journal under their own request group, deletion cleanup.
- `src/tests/HSMServer.Core.Tests/Model/ManagementApi/JournalAdministrationServiceTests.cs` — the unified-journal contract (#1588): kind-stamped records linked to the request group (policy, template and web-editor full-list writes), rollback by record (undo + the redo record in the journal), the admin gate (a non-admin token owner is Forbidden with the admin message), non-rollbackable records answer Conflict, list filters by kind/product, details carry the undo group, `RequestGroupId` minted once per HttpContext.
- `src/tests/HSMServer.Core.Tests/Controllers/SensorPoliciesApiControllerTests.cs` — the dry-run transport header (`X-HSM-Dry-Run` answers the preview without applying; no grouping header exists anymore).
- `HsmMcpServerRegistrationTests` / `HsmMcpWireTests` — the journal tools in the spec-tool pins; write tools carry no `changeSetId` argument anymore.
- `src/tests/Autotests/journal/journal.spec.ts` — Playwright: menu link, page render, table shell, the kind/product filters, and the admin-only rollback gating (the seeded viewer sees no Rollback controls). The full rollback-dialog flow needs a journaled write, which the cookie session cannot produce through the management API — that flow is covered at the service seam.

## Conventions to keep

- New journal behavior gets a service-seam test first (apply → journal → roll back → observe state); internal journal internals are never asserted from transport tests.
- Any new journaled write endpoint updates the dry-run carrier and the journal-record link assertions (kind + group).
- Conflict-rule changes must keep the "identical re-assert is not a conflict" test green.
- Admin-gating changes must keep `Rollback_ByRecord_NonAdminTokenOwner_IsForbidden` green (Manager tokens are denied).
