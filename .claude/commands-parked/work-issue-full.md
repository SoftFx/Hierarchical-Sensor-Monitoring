# /work-issue — Start working on a GitHub issue with branch and draft PR

Start working on the GitHub issue specified in $ARGUMENTS (issue number).

## Instructions

1. **Fetch the issue** — run `gh issue view $ARGUMENTS --json number,title,labels,body` to get full details.

2. **Plan the fix** — read the issue body, explore the referenced files, and propose an implementation approach. Use plan mode if the change is non-trivial. Present the plan to the user for approval before writing code.

3. **Create a feature branch** from `master`. First sync local master with the remote — a stale local master is the commonest cause of fixes that target a UI or API that no longer exists on `origin/master` (learned on #1297, where local master was 10+ commits behind and the fix landed against a UI that had already been replaced by a squash-merged PR the day before):
   ```
   git fetch origin master
   git checkout master
   git pull --ff-only           # refuses if local master has diverged; never creates a merge commit
   git checkout -b feature/$ARGUMENTS-<short-slug> master
   ```
   Where `<short-slug>` is a 2-4 word kebab-case summary of the issue (e.g., `feature/1076-alert-schedule-unique-name`).
   Sanity-check before committing: if the issue references specific UI patterns, file paths, or function signatures, confirm they exist at the chosen base. A mismatch usually means the base is stale — re-fetch and re-branch.

4. **Implement the fix** — make the code changes according to the approved plan. Keep changes focused on the issue scope. Build to verify compilation.

5. **Commit and push**:
   ```
   git add <changed files>
   git commit -m "Short description of the change

   Longer explanation if needed.

   Closes #$ARGUMENTS

   Co-Authored-By: Claude Opus 4.7 <noreply@anthropic.com>"
   git push -u origin feature/$ARGUMENTS-<short-slug>
   ```

6. **Create a draft PR** linked to the issue:
   ```
   gh pr create --draft --base master --title "Short description" --body "$(cat <<'EOF'
   ## Summary
   <1-2 sentences on what this does>

   ## Test plan
   - [ ] <manual verification steps from acceptance criteria>

   Closes #$ARGUMENTS

   🤖 Generated with [Claude Code](https://claude.com/claude-code)
   EOF
   )"
   ```

7. **Report back** — share the branch name and PR URL with the user.

## Guidelines

- If $ARGUMENTS is empty, ask the user for the issue number
- Always create the branch from `master` unless the issue specifies otherwise
- The commit message should end with `Closes #$ARGUMENTS` to auto-link
- Use draft PR so it doesn't trigger full CI until ready
- Keep the PR title under 70 characters
- If the issue references specific files, read them before planning
- If the user just wants the branch and PR without implementation (e.g., to code it themselves), skip steps 2 and 4 — just create the branch, push, and open the draft PR with a placeholder body
