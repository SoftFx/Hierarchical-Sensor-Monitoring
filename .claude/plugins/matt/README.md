# `matt` — vendored engineering skills

Matt Pocock's skills, copied into this repo so they are reviewed, versioned and
shared like any other source. Upstream: <https://github.com/mattpocock/skills>,
plugin `mattpocock-skills` v1.2.0, commit `2ab9580`. The upstream `teach` skill
is deliberately not vendored — it turns the working directory into a learning
workspace, which does not belong in a code repo.

Skills load namespaced: `matt:tdd`, `matt:code-review`, `matt:grilling`. The
namespace is what keeps `matt:code-review` from colliding with Claude Code's own
`/code-review`. Upstream, the skills refer to each other by bare slash — `/tdd`,
`/code-review` — which under a namespace resolves to whatever else owns that name;
those references are rewritten to `/matt:<name>` here, and any future snapshot
needs the same pass. `LICENSE` is upstream's, carried with the copy as MIT requires.

Wiring: [`/.claude-plugin/marketplace.json`](../../../.claude-plugin/marketplace.json)
lists this plugin, [`/.claude/settings.json`](../../settings.json) declares the
marketplace and enables `matt@hsm`. Nothing is installed by hand — the prompt
appears when you trust the repo folder.

## This is a trial

The whole set is enabled on purpose: we are evaluating whether this way of working
beats what we do today, and half a set answers nothing. Two consequences while the
trial runs:

- **Our overlapping slash-commands are parked**, not deleted —
  [`.claude/commands-parked/`](../../commands-parked) holds `create-issue.md` and
  `work-issue-full.md`, and Claude Code does not scan it. Move them back to
  `.claude/commands/` to end the trial. `/release-collector` and
  `/release-server` stay live; nothing here replaces them.
- **One gap is filled rather than trialled.** Issue creation maps cleanly onto
  `matt:to-spec`, `matt:to-tickets`, `matt:wayfinder` and `matt:triage`, but no
  skill in the set touches branches, draft PRs or `Closes #N`: `matt:implement`
  takes a spec as given and stops at "commit". So `/work-issue` stays live in a
  thin form — fresh-master sync, branch naming, draft PR, hand-off — and delegates
  the thinking to the trialled skills. Those mechanics are repo policy, not
  methodology, and the stale-master rule in it was paid for on #1297.
- **The upstream document layout is left as the author wrote it.** These skills
  expect one `CONTEXT.md` at the repo root and `docs/adr/`; this repo keeps its
  canon in `aicontext/` and `docs/decisions/`. We deliberately did not repoint
  them, so the trial shows the workflow as designed. Expect a parallel `CONTEXT.md`
  and `docs/adr/` to appear once `domain-modeling` or `setup-matt-pocock-skills`
  runs — reconciling or reverting that is a decision for the end of the trial.

## Turning a skill off

Everything under `skills/` loads; everything under `skills-off/` does not. The
`skills` field in `plugin.json` is *not* a filter — the directory is scanned
regardless, so moving the folder is the only switch.

```bash
git mv .claude/plugins/matt/skills/wayfinder .claude/plugins/matt/skills-off/
```

Back on: move it the other way. Both directions are a reviewable diff.

## When changes take effect

The plugin's version is the repo's HEAD commit, so the runtime picks up changes
**after they are committed** — an edited or moved skill in a dirty working tree
still runs from the old copy in `~/.claude/plugins/cache/hsm/matt/<sha>/`.

To try a change before committing, force a re-copy:

```bash
claude plugins uninstall matt@hsm --scope project && claude plugins install matt@hsm --scope project
```

## Before pulling upstream changes

These skills are instructions an agent executes, and the plugin format also
allows hooks and MCP servers — treat `.claude/plugins/**` as code in review, not
as documentation.
