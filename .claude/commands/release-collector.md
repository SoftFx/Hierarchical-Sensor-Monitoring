# /release-collector <optional-version> — Cut a DataCollector NuGet release

Cut a release for the C# HSMDataCollector (published as the
`HSMDataCollector.HSMDataCollector` NuGet package). Reads the current version,
proposes the next patch, bumps the csproj, regenerates `ReleaseNote.Collector.md`
from commits since the last `collector-v*` tag (or, on the first run, from the
commit that last bumped `<Version>`), opens a PR, then runs
`collector-nuget-build.yml` on master after the user merges. Finally tags
`collector-v<VERSION>` for the next release's baseline.

Native (C++) collector is out of scope — this only ships the managed NuGet.

## Instructions

1. **Verify clean state** — run `git status --porcelain`. If there are
   uncommitted changes, stop and ask the user to commit or stash.

2. **Read current version** — read
   `src/collector/HSMDataCollector/HSMDataCollector.csproj` and extract the
   value of `<Version>...</Version>` (e.g. `3.4.12`). The same value also
   appears in `<AssemblyVersion>`, `<AssemblyFileVersion>`, and
   `<ProductVersion>` — bump all four together so the package and assembly
   metadata stay in sync.

3. **Propose next version** — compute PATCH+1 (e.g. `3.4.12` → `3.4.13`). If
   `$ARGUMENTS` is non-empty and looks like a semver (`X.Y.Z`), use it instead.
   Use AskUserQuestion to confirm:
   - Show the current version in the question text.
   - Offer the proposed next version as the first option labeled `(Recommended)`.
   - The user can pick "Other" to enter a custom version.
   - In the same call, ask whether this is a Final release or a Pre-release
     (see Guidelines — pre-release means appending a `-preview` suffix to the
     NuGet version; the workflow has no flag of its own).
   Do not proceed until the user explicitly confirms.

4. **Sync and branch from master**:
   ```
   git checkout master && git pull --ff-only && git fetch --tags
   git checkout -b release/collector-v<NEW>
   ```

5. **Bump version** — in
   `src/collector/HSMDataCollector/HSMDataCollector.csproj` replace all four
   occurrences of the old version (`<Version>`, `<AssemblyVersion>`,
   `<AssemblyFileVersion>`, `<ProductVersion>`) with the new value. Do not touch
   `HSMServer.csproj` or `HSMSensorDataObjects.csproj` — those are separate
   releases (see CLAUDE.md Versioning). For a pre-release, set only `<Version>`
   to `<NEW>-preview` (NuGet accepts prerelease suffix there); leave the other
   three numeric so the assembly file version stays clean.

6. **Collect commits since the last release** — find the most recent
   `collector-v*` tag that matches the modern 3.x line:
   ```
   git tag --sort=-creatordate | grep "^collector-v3" | head -1
   ```
   List commit subjects between that tag and master, scoped to collector code:
   ```
   git log --pretty=format:"- %s" collector-v<OLD>..master -- src/collector
   ```
   If no `collector-v3*` tag exists yet (first run of this skill), fall back to
   the commit where `<Version>` was last bumped:
   ```
   git log -1 --format="%H" -- src/collector/HSMDataCollector/HSMDataCollector.csproj
   ```
   then `git log --pretty=format:"- %s" <SHA>..master -- src/collector`. Warn
   the user this looks like the first tagged collector release.

7. **Write `ReleaseNote.Collector.md`** — produce a concise, package-consumer-
   facing release note in this format:
   ```
   # HSM DataCollector

   ## <Area>
   * <change>

   ## <Area>
   * <change>
   ```
   The audience is .NET developers consuming the NuGet package, not server
   operators. Selection rules:
   - **Keep**: new default sensors, public API additions/changes, behavioral
    changes (retry policy, queueing, shutdown), bug fixes that affect telemetry
    correctness or reliability, native parity work that changes what the managed
    package emits.
   - **Drop**: pure refactors, internal cleanups, CI-only changes, test-only
    changes, native-only C++ internals (unless they change wire format or
    default-sensor surface), doc-only changes (`aicontext/`, `CLAUDE.md`).
   - **Call out default-sensor additions explicitly** — consumers using
    `AddAllComputerSensors` will see new sensors after upgrading.
   - **Lead with the most impactful change** in each area; merge related commits
    under one bullet.
   - **Hard limit: 2000 characters total** (count the rendered markdown bytes
    before writing). If over budget, drop the lowest-impact bullets first and
    re-count. Never ship a note over 2000.

8. **Commit and push**:
   ```
   git add src/collector/HSMDataCollector/HSMDataCollector.csproj ReleaseNote.Collector.md
   git commit -m "Release HSMDataCollector <NEW>"
   git push -u origin release/collector-v<NEW>
   ```

9. **Open PR** — create a PR targeting master:
   ```
   gh pr create --base master --title "Release HSMDataCollector <NEW>" --body "$(cat <<'EOF'
   Bumps `<OLD>` → `<NEW>` and regenerates `ReleaseNote.Collector.md`.

   After merge, `/release-collector` will trigger `collector-nuget-build.yml`,
   which publishes `HSMDataCollector.HSMDataCollector` `<NEW>` to nuget.org and
   automatically tags `collector-v<NEW>`.

   🤖 Generated with [Claude Code](https://claude.com/claude-code)
   EOF
   )"
   ```
   Share the PR URL. **STOP** — do not merge. Wait for the user to review and
   merge explicitly.

10. **Wait for user confirmation** — pause here. When the user confirms the PR
    has been merged, sync master and verify the bump landed:
    ```
    git checkout master && git pull --ff-only
    grep "<Version>" src/collector/HSMDataCollector/HSMDataCollector.csproj
    ```

11. **Run the build workflow** — trigger the NuGet build on master:
    ```
    gh workflow run collector-nuget-build.yml --ref master
    ```
    Then locate the run and watch it:
    ```
    RUN_ID=$(gh run list --workflow collector-nuget-build.yml --branch master --limit 1 --json databaseId --jq '.[0].databaseId')
    gh run watch "$RUN_ID" --exit-status
    ```
    The workflow packs `HSMDataCollector.HSMDataCollector`, pushes it to
    https://api.nuget.org/v3/index.json using the `NUGETKEY` secret from the
    `Nuget` environment, and then (in a follow-up `create-tag` job) pushes the
    `collector-v<NEW>` tag — that tag is the baseline the next release diff
    runs against. On success, verify the package appears on the gallery:
    ```
    https://www.nuget.org/packages/HSMDataCollector.HSMDataCollector/<NEW>
    ```
    and confirm the tag landed:
    ```
    git fetch --tags && git tag --list "collector-v<NEW>"
    ```

## Guidelines

- Never merge the PR yourself. Wait for explicit user confirmation.
- `ReleaseNote.Collector.md` lives at the repo root and is separate from
  `ReleaseNote.md` (server). It is for human/historical reference only — the
  NuGet package does not consume it at build time. The package's `<Description>`
  in csproj is what shows on nuget.org as the long description.
- Tags follow `collector-v<VERSION>` (e.g. `collector-v3.4.13`). The workflow's
  `create-tag` job creates the tag automatically **after** the NuGet push
  succeeds — do not tag manually. Pre-1.0 `collector-v0.*` and
  `collector-v2.1.*` tags are 2022-era artifacts from a different versioning
  scheme and must not be used as the baseline.
- The NuGet version may carry a prerelease suffix (`3.4.13-preview`). NuGet
  treats `3.4.13-preview` as older than `3.4.13`, so a final release later will
  surface as the stable version. Tag prereleases as
  `collector-v<VERSION>-preview` to keep history honest.
- If `gh run watch` reports a failure, investigate with
  `gh run view <RUN_ID> --log-failed` and report the failing step. Common
  causes: `NUGETKEY` secret rotated or `Nuget` environment approval pending.
  Do not retry blindly — fix the root cause.
- If the user supplied `$ARGUMENTS` as a version, still confirm via
  AskUserQuestion before bumping — typos in a release version are expensive,
  and a bad version pushed to nuget.org cannot be reused or deleted (only
  unlisted).
- **Publishing to nuget.org is irreversible.** A version once pushed can be
  unlisted but never deleted or re-uploaded. If the build fails AFTER the push
  step succeeds, do not silently re-run — confirm with the user how to proceed.
- The skill assumes `master` is the default branch and the source of truth for
  releases. If the repo has been reconfigured to use `main` or a release branch,
  stop and ask.
- This skill is **C# / managed only**. The native C++ collector, the
  `hsm-collector-registry` vcpkg workflow, and the wrapper release are separate
  and out of scope. If the user asks for those, do not improvise — point them
  at the relevant workflow file.
