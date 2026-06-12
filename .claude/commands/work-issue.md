# /work-issue — Start working on a GitHub issue with branch and draft PR

Start working on the GitHub issue specified in $ARGUMENTS (issue number).

## Instructions

1. **Fetch the issue** — run `gh issue view $ARGUMENTS --json number,title,labels,body` to get full details.

2. **Plan the fix** — read the issue body, explore the referenced files, and propose an implementation approach. Use plan mode if the change is non-trivial. Present the plan to the user for approval before writing code.

3. **Create a feature branch** from `master`:
   ```
   git checkout -b feature/$ARGUMENTS-<short-slug> master
   ```
   Where `<short-slug>` is a 2-4 word kebab-case summary of the issue (e.g., `feature/1076-alert-schedule-unique-name`).

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
