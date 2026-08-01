# /work-issue — branch, PR and hand-off mechanics for a GitHub issue

Take the GitHub issue in $ARGUMENTS (issue number) from "not started" to "draft PR open".

**This command owns the git mechanics only.** The thinking — how to solve it, in what order, test-first or not — belongs to the skills being trialled: `/matt:implement` for work that already has a spec or tickets, `/matt:grilling` when the approach is not settled, `/matt:tdd` at the seams, `/matt:diagnosing-bugs` when the issue is a defect. This command exists because none of them touch branches, draft PRs or `Closes #N`, and because the rules below were learned the hard way in this repo.

## 1. Read the issue

```
gh issue view $ARGUMENTS --json number,title,labels,body --comments
```

If the issue carries an agent brief from `/matt:triage`, that brief is the spec — do not re-derive it.

## 2. Branch from a fresh master

A stale local master is the commonest cause of fixes that target code that no longer exists. On #1297 local master was 10+ commits behind and the fix landed against a UI a squash-merged PR had already replaced the day before.

```
git fetch origin master
git checkout master
git pull --ff-only           # refuses if local master diverged; never creates a merge commit
git checkout -b feature/$ARGUMENTS-<short-slug> master
```

`<short-slug>` is a 2–4 word kebab-case summary, e.g. `feature/1076-alert-schedule-unique-name`.

Before writing code, confirm that the UI patterns, file paths or signatures the issue names actually exist at this base. A mismatch means the base is stale — re-fetch and re-branch.

## 3. Do the work

Hand over to the trialled skills. Keep the change inside the issue's scope; build to verify compilation.

## 4. Commit and push

```
git add <changed files>
git commit -m "Short description of the change

Longer explanation if needed.

Closes #$ARGUMENTS

Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>"
git push -u origin feature/$ARGUMENTS-<short-slug>
```

`Closes #N` goes in before the PR is merged — added afterwards it does not close the issue.

## 5. Open a draft PR

```
gh pr create --draft --base master --title "Short description" --body "..."
```

Then stop. Never merge to `master` — review and merge are the human's call, green checks are not permission.
