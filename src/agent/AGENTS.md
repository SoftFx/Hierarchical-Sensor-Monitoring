# HSM Agent — Codex Instructions

Directory-scoped rules for `src/agent/` (the native Windows self-update HSM Agent). These supplement
the root [`AGENTS.md`](../../AGENTS.md); where a rule here refines the root, the more specific rule wins.

## Versioning — the agent version is the self-update delivery key

The agent version is **not** just a release-time package number — it is the value the self-update
channel compares to decide whether a deployed agent receives a new binary.

- **Location:** `project(HsmAgent VERSION x.y.z ...)` in [`CMakeLists.txt`](CMakeLists.txt), compiled in
  as `HSM_AGENT_VERSION`.
- **Rule:** whenever a change alters the *shipped agent binary's* observable behavior, **bump the patch
  version in the same PR.** This explicitly includes changes that originate in a dependency compiled
  into the agent — above all the native collector (`src/native/collector`): a collector-only change
  (new/changed log events, a sensor fix, a transport change) still produces a different agent binary
  and MUST bump `HsmAgent VERSION` to reach already-deployed agents. This overrides the root
  "Versioning" rule ("bump only when preparing a release") for this directory.
- **Why:** self-update ships a binary only when the server-advertised version is **strictly greater**
  than the running one — `src/update_checker.cpp` compares semver major → minor → patch; the server
  advertises `GET /api/agent/version` from its staged `wwwroot/agent/`. A same-version rebuild, even
  with real behavior changes, is invisible: every deployed agent reports "already up to date" and
  never receives it.
- **Exception:** pure test-only, doc-only, or behavior-neutral refactors that leave the shipped
  binary's observable behavior identical do not need a bump.

**Precedent:** the #1221 collector logging events did not bump `HsmAgent VERSION` (stayed `0.5.24`),
so a rebuilt agent could only be delivered by a manual binary swap — self-update saw `0.5.24 == 0.5.24`
and declined. Bumping to `0.5.25` in that PR would have let the running agents pick it up on their own.
