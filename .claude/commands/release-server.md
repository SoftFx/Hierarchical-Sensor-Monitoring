# /release-server — Cut an HSMServer release

Cut a release for HSMServer. Reads the current version, proposes the next patch,
bumps the csproj, regenerates `ReleaseNote.md` from commits since the last
`server-v*` tag, opens a PR, then runs the build workflow on master after the
user merges.

## Instructions

1. **Verify clean state** — run `git status --porcelain`. If there are uncommitted
   changes, stop and ask the user to commit or stash before starting a release.

2. **Read current version** — read `src/server/HSMServer/HSMServer.csproj` and
   extract the value of `<Version>...</Version>`. The format is
   `MAJOR.MINOR.PATCH` (e.g. `3.40.32`).

3. **Propose next version** — compute PATCH+1 from the current version
   (e.g. `3.40.32` → `3.40.33`). If `$ARGUMENTS` is non-empty and looks like a
   semver (`X.Y.Z`), use it instead. Use AskUserQuestion to confirm:
   - Show the current version in the question text.
   - Offer the proposed next version as the first option labeled `(Recommended)`.
   - The user can pick "Other" to enter a custom version.
   - In the same call, ask whether this is a Final release or a Pre-release
     (controls the `isPreRelease` input to the build workflow).
   Do not proceed until the user explicitly confirms.

4. **Sync and branch from master**:
   ```
   git checkout master && git pull --ff-only && git fetch --tags
   git checkout -b release/server-v<NEW>
   ```

5. **Bump version** — edit `src/server/HSMServer/HSMServer.csproj` and replace
   `<Version>OLD</Version>` with `<Version>NEW</Version>`. Do not touch
   `HSMDataCollector.csproj` or `HSMSensorDataObjects.csproj` — those are
   separate releases (see AGENTS.md Versioning).

6. **Collect commits since the last release** — find the most recent `server-v*`
   tag:
   ```
   git tag --sort=-creatordate | grep "^server-v" | head -1
   ```
   List commit subjects between that tag and master:
   ```
   git log --pretty=format:"- %s" server-v<OLD>..master
   ```
   If no prior `server-v*` tag exists, fall back to `git log master` from the
   initial commit and warn the user that this looks like the first release.

7. **Write `ReleaseNote.md`** — produce a concise, user-facing release note in
   the existing format:
   ```
   # HSM Server

   ## <Area>
   * <change>

   ## <Area>
   * <change>
   ```
   Selection rules:
   - **Keep**: new features, user-visible bug fixes, behavior changes, UI changes,
     schema/storage changes that operators need to know about.
   - **Drop**: pure refactors, internal cleanups, CI-only changes, test-only
     changes, doc-only changes (`aicontext/`, `AGENTS.md`).
   - **Lead with the most impactful change** in each area; merge related commits
     under one bullet.
   - **Hard limit: 2000 characters total** (count the rendered markdown bytes
     before writing). If over budget, drop the lowest-impact bullets first and
     re-count. Never ship a note over 2000.

8. **Commit and push**:
   ```
   git add src/server/HSMServer/HSMServer.csproj ReleaseNote.md
   git commit -m "Release HSMServer <NEW>"
   git push -u origin release/server-v<NEW>
   ```

9. **Open PR** — create a PR targeting master with a short body that links the
   previous tag and lists what's in the release:
   ```
   gh pr create --base master --title "Release HSMServer <NEW>" --body "$(cat <<'EOF'
   Bumps `<OLD>` → `<NEW>` and regenerates `ReleaseNote.md`.

   After merge, `/release-server` will trigger `server-build.yml` with
   `isPreRelease=<true|false>`.

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
    grep "<Version>" src/server/HSMServer/HSMServer.csproj
    ```

11. **Run the build workflow** — trigger the release build on master:
    ```
    gh workflow run server-build.yml --ref master -f isPreRelease=<true|false>
    ```
    Then locate the run and watch it:
    ```
    RUN_ID=$(gh run list --workflow server-build.yml --branch master --limit 1 --json databaseId --jq '.[0].databaseId')
    gh run watch "$RUN_ID" --exit-status
    ```
    On success the workflow creates the `server-v<NEW>` tag and publishes a
    GitHub release whose body is `ReleaseNote.md`. Share the release URL:
    ```
    gh release view server-v<NEW> --json url --jq .url
    ```

## Guidelines

- Never merge the PR yourself. Wait for explicit user confirmation.
- `ReleaseNote.md` lives at the repo root and is read by
  `ncipollo/release-action` from the workflow. It must be on master before
  `gh workflow run` fires.
- Tags follow `server-v<VERSION>` (e.g. `server-v3.40.31`). The workflow creates
  the tag automatically — do not tag manually.
- `isPreRelease=true` skips pushing the `latest` Docker tag and labels the
  GitHub release as a pre-release. Use it for verification builds before a
  final release.
- If `gh run watch` reports a failure, investigate with
  `gh run view <RUN_ID> --log-failed` and report the failing step. Do not retry
  blindly — fix the root cause.
- If the user supplied `$ARGUMENTS` as a version, still confirm via
  AskUserQuestion before bumping — typos in a release version are expensive.
- The skill assumes `master` is the default branch and the source of truth for
  releases. If the repo has been reconfigured to use `main` or a release branch,
  stop and ask.
