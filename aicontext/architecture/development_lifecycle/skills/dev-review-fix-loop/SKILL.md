---
name: dev-review-fix-loop
description: Run a development review/fix loop for an existing branch or PR by launching role-based subagents, applying blocker fixes, rerunning focused review when useful, updating docs/tests, and preparing a final handoff. Use when the user asks for review/fix loop, subagent review pack, repeated code review, PR hardening, or "review and fix until ready".
---

# Dev Review Fix Loop

Use this skill to orchestrate role-based review subagents, fix confirmed blockers,
and repeat selectively. The initial pass may use a role pack; repeat passes must
be focused by the orchestrator and should not automatically rerun every subagent.

## Inputs

Determine:

- target: current branch, PR number, or explicit commit range;
- repository root and clean/dirty worktree state;
- available review roles under `aicontext/architecture/development_lifecycle/review_roles/`;
- changed files, PR comments, review threads, and failing checks when available.

If the target is ambiguous and cannot be inferred from branch or PR metadata, ask one
short clarification.

## Review Role Selection

Read `review_roles/index.md` and `common.md`. Select the smallest useful pack:

- Collector SDK: `collector`, `testing`, `performance`, `docs`; add `integrations` for public API or wrapper changes.
- Server/API: `server`, `database`, `testing`, `observability`; add `ux` for site-visible changes.
- Storage/cache: `database`, `performance`, `testing`, `observability`.
- Site/UI/alerts: `ux`, `server`, `testing`, `docs`.
- Docs-only: `docs`; add the domain role when docs describe behavior.

Launch role reviewers as independent subagents when subagent tools are available.
Use explorer agents for read-only reviews. Use worker agents only for clearly
disjoint fixes with explicit file ownership.

The selected role pack is for the initial review pass only by default. Later
passes are not pack reruns; they are orchestrator-selected focused checks.

## Subagent Prompt

Use the template in `references/subagent-prompts.md`. Give each reviewer:

- the target PR/branch/commit range;
- the specific role file path and `common.md`;
- changed-file scope or paths to inspect;
- instruction to return findings first with `P0`-`P3`, file/line refs, tests reviewed, residual risks;
- instruction not to edit files unless explicitly assigned as a worker.

Run independent reviewers in parallel when possible.

## Main Loop

1. Snapshot status: branch, upstream, dirty files, changed files, existing PR comments, and checks.
2. Launch selected review subagents.
3. While reviewers run, do non-overlapping local inspection: build/test failures, obvious docs gaps, and current diff sanity.
4. Merge findings into one blocker list:
   - fix `P0`/`P1` unless invalid;
   - fix `P2` when it is a real regression, race, missing contract, operational risk, or cheap hardening;
   - leave `P3` unless requested or already touching the same line.
5. Apply fixes locally. Do not revert unrelated user changes. Keep edits scoped to review findings.
6. Run targeted tests first, then broader tests if shared behavior changed.
7. Decide whether to repeat:
   - never rerun the full initial pack by default;
   - compare the fix diff against the role map and select only roles whose risk area changed;
   - repeat a focused subagent review when fixes touch shared lifecycle, storage format, public API, auth/security, concurrency, alerting, or broad UI behavior;
   - repeat the same role that found a blocker when the fix is non-trivial and needs validation;
   - skip subagent re-review when the fix is mechanical, covered by targeted tests, or confined to low-risk docs/comment polish;
   - do not repeat only to chase minor `P3` polish, already-invalid findings, or unchanged areas.
8. Stop when no unresolved `P0`/`P1` remains and remaining `P2` risks are either fixed, documented as residual risk, or explicitly deferred.
9. Commit/push/comment only when the user asked for that workflow or repo lifecycle expects it.
10. Final handoff: summarize fixes, tests, unresolved risks, and remote check status.

## Orchestrator Judgment

The orchestrator owns the loop decision and the repeat-pass role selection.
Prefer one broad review pass followed by zero or one focused re-review of changed
risk areas. More passes are useful only when new substantial findings keep
appearing.

Repeat-pass examples:

- Fix only collector scheduler concurrency: rerun `collector` and maybe `performance`; do not rerun `docs`, `ux`, `server`, or `database`.
- Fix public DTO serialization: rerun `docs`/API contract and the owning HSM Server/API role; do not rerun unrelated UI or storage roles.
- Fix a typo, comment, or PR description: do not launch subagents; verify locally and hand off.
- Fix storage key format or cache recovery: rerun `database` and `testing`; add `performance` only if scan/query shape changed.

Never merge to `master`/`main`. Stop at human handoff.

## Documentation Discipline

When code changes affect documented behavior, update the nearest canonical docs:

- feature docs under `aicontext/features/...`;
- role/process docs under `aicontext/architecture/development_lifecycle/...`;
- glossary terms in `aicontext/glossary.md`;
- public wiki/API docs when behavior is user- or integrator-visible.

If the correct feature folder does not exist, create it from
`aicontext/features/_TEMPLATE_feature.md`.
