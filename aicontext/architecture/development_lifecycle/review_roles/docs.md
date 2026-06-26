# Documentation Review Roles

## Docs / Wiki Reviewer

Focus:

- user-facing wiki docs, `ai-docs`, `wiki-draft`, README, release notes, and test docs;
- whether docs match implemented server, collector, alert, UI, and API behavior;
- whether examples compile or remain plausible for current public APIs;
- terminology consistency for sensors, paths, modules, environments, alerts, schedules, statuses, and access keys.

Must read:

- all docs changed in the PR;
- implementation only enough to verify behavior;
- related existing wiki pages in English/Russian when the PR changes documented behavior.

Output:

- stale or conflicting docs;
- missing operator/integrator guidance;
- broken links, outdated screenshots, or invalid examples;
- recommended doc placement and wording.

---

## API Contract Reviewer

Focus:

- public DTOs in `HSMSensorDataObjects`, collector interfaces/options, Swagger comments, wrapper headers, and sample code;
- compatibility of request/response semantics between collector, server, C++ wrapper, and docs;
- error semantics, default values, enum additions, nullability, and serialization names.

Must read:

- changed API objects, public collector interfaces, Swagger XML, wrappers, and docs;
- tests or samples that exercise the contract.

Output:

- breaking contract risks;
- missing docs/examples for new or changed public API;
- tests needed to lock serialization and compatibility.
