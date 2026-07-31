# /create-issue — Create a GitHub issue following the project's format

Create a GitHub issue for the topic described in $ARGUMENTS.

## Instructions

1. **Research existing issues** — run `gh issue list --state all --limit 30 --json number,title,labels` to:
   - Check for duplicates
   - Learn the project's issue format conventions (section structure, labeling patterns, detail level)

2. **Gather available labels** — run `gh label list --json name,description` to pick appropriate labels.

3. **Explore relevant code** — use Grep/Glob/Read to find the source files related to the issue topic so you can include accurate file references.

4. **Generate issue body** using the project's standard pattern:
   ```
   ## Context
   (Why this issue exists, what prompted it, current state)

   ## Goals / Proposed scope
   (What needs to be done)

   ## Tasks
   - [ ] Actionable checklist items

   ## Acceptance criteria
   (Clear, testable conditions for completion)

   ## Out of scope
   (What this issue does NOT cover, if applicable)

   ## References
   - File: `path/to/relevant/file.cs`
   - Related issue: #NNN
   ```

5. **Pick labels** — choose from the project's existing labels (common: `epic`, `collector`, `enhancement`, `bug`, `refactoring`, `ai`, `codex`). Use `epic` for large multi-part issues.

6. **Create the issue** — run:
   ```
   gh issue create --title "Title Here" --label "label1" --label "label2" --body "$(cat <<'EOF'
   ... issue body ...
   EOF
   )"
   ```

7. **Report back** — share the created issue URL with the user.

## Guidelines

- Write in English
- Use concrete file paths and issue numbers in references
- Tasks should be actionable checkbox items, not vague goals
- Match the detail level of existing project issues (see #1055–#1063 for examples)
- If $ARGUMENTS is empty, ask the user what the issue should be about
