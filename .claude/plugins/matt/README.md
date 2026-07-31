# `matt` — vendored engineering skills

Matt Pocock's skills, copied into this repo so they are reviewed, versioned and
shared like any other source. Upstream: <https://github.com/mattpocock/skills>,
plugin `mattpocock-skills` v1.2.0, commit `2ab9580`. The upstream `teach` skill
is deliberately not vendored — it turns the working directory into a learning
workspace, which does not belong in a code repo.

Skills load namespaced: `matt:tdd`, `matt:code-review`, `matt:grilling`. The
namespace is what keeps `matt:code-review` from colliding with Claude Code's own
`/code-review`.

Wiring: [`/.claude-plugin/marketplace.json`](../../../.claude-plugin/marketplace.json)
lists this plugin, [`/.claude/settings.json`](../../settings.json) declares the
marketplace and enables `matt@hsm`. Nothing is installed by hand — the prompt
appears when you trust the repo folder.

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
